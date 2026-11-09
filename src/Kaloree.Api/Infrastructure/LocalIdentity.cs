using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Http.Metadata;
using System.Text.Json.Serialization;

namespace Kaloree.Api.Infrastructure;

public static class LocalIdentity
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PasswordChangeInput(string OldPassword, string NewPassword);

    public static void MapLocalIdentity(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        group.AddEndpointFilter(async (context, next) =>
        {
            foreach (var argument in context.Arguments)
            {
                var invalid = argument switch
                {
                    RegisterRequest r => string.IsNullOrEmpty(r.Email) || r.Email.Length > 256 || string.IsNullOrEmpty(r.Password),
                    LoginRequest r => string.IsNullOrEmpty(r.Email) || r.Email.Length > 256 || string.IsNullOrEmpty(r.Password),
                    RefreshRequest r => string.IsNullOrEmpty(r.RefreshToken) || r.RefreshToken.Length > 16384,
                    PasswordChangeInput r => r.OldPassword?.Length > 1024,
                    _ => false
                };
                if (invalid) return Results.Problem(statusCode: 400, title: "invalid_request");
                var password = argument switch
                {
                    RegisterRequest r => r.Password,
                    LoginRequest r => r.Password,
                    PasswordChangeInput r => r.NewPassword,
                    _ => null
                };
                if (password?.Length > 1024) return Results.Problem(statusCode: 400, title: "password_too_long");
            }
            return await next(context);
        });
        // Keep framework login, refresh and authenticator handling. Disable its email-dependent
        // endpoints and replace registration/password changes with flows that never send email.
        group.MapIdentityApi<IdentityUser>().Add(endpoint =>
        {
            if (endpoint is not RouteEndpointBuilder route) return;
            var path = route.RoutePattern.RawText?.ToLowerInvariant();
            var post = endpoint.Metadata.OfType<HttpMethodMetadata>().Any(m => m.HttpMethods.Contains("POST"));
            if (path is "/api/v1/auth/register" or "/api/v1/auth/confirmemail" or
                "/api/v1/auth/resendconfirmationemail" or "/api/v1/auth/forgotpassword" or
                "/api/v1/auth/resetpassword" || (path == "/api/v1/auth/manage/info" && post))
                endpoint.Metadata.Add(new DisabledEndpoint());
        });
        group.MapPost("/register", Register).Produces(200).ProducesValidationProblem();
        group.MapPost("/manage/info", ChangePassword).RequireAuthorization()
            .Produces<InfoResponse>().ProducesValidationProblem();
        group.MapPost("/logout", async (ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            // Sign out all devices, including outstanding refresh tokens.
            var result = await users.UpdateSecurityStampAsync(user);
            return result.Succeeded ? Results.NoContent() : Results.Conflict();
        }).RequireAuthorization();
    }

    public static async Task ValidateLocalIdentity(HttpContext h, RequestDelegate next)
    {
        if (h.User.Identity?.IsAuthenticated == true &&
            h.User.Identity.AuthenticationType == IdentityConstants.ApplicationScheme)
        {
            var signIn = h.RequestServices.GetRequiredService<SignInManager<IdentityUser>>();
            var user = await signIn.ValidateSecurityStampAsync(h.User);
            if (user is null)
            {
                h.Response.StatusCode = 401;
                return;
            }
            ((ClaimsIdentity)h.User.Identity).AddClaim(new Claim("sub", "local:" + user.Id));
        }

        if (h.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            // This API accepts bearer tokens only; browser cookies would need a separate CSRF design.
            if ((bool.TryParse(h.Request.Query["useCookies"], out var cookies) && cookies) ||
                (bool.TryParse(h.Request.Query["useSessionCookies"], out var sessionCookies) && sessionCookies))
            {
                h.Response.StatusCode = 400;
                return;
            }
            var quota = h.RequestServices.GetRequiredService<Quota>();
            var now = DateTimeOffset.UtcNow;
            var ip = h.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!await quota.Take($"auth:{ip}:{now:yyyyMMddHHmm}", 20, now.AddMinutes(2), h.RequestAborted))
            {
                h.Response.StatusCode = 429;
                return;
            }
        }
        await next(h);
    }

    private static async Task<IResult> Register(RegisterRequest input, UserManager<IdentityUser> users)
    {
        var user = new IdentityUser { UserName = input.Email, Email = input.Email };
        var result = await users.CreateAsync(user, input.Password);
        return result.Succeeded ? Results.Ok() : IdentityErrors(result);
    }

    private static async Task<IResult> ChangePassword(PasswordChangeInput input, ClaimsPrincipal principal, UserManager<IdentityUser> users)
    {
        if (string.IsNullOrEmpty(input.OldPassword) || string.IsNullOrEmpty(input.NewPassword))
            return Results.Problem(statusCode: 400, title: "current_and_new_password_required");
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        var result = await users.ChangePasswordAsync(user, input.OldPassword, input.NewPassword);
        if (!result.Succeeded) return IdentityErrors(result);
        return Results.Ok(new InfoResponse { Email = user.Email!, IsEmailConfirmed = user.EmailConfirmed });
    }

    private static IResult IdentityErrors(IdentityResult result) => Results.ValidationProblem(
        result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));

    private sealed class DisabledEndpoint : ISuppressMatchingMetadata, IExcludeFromDescriptionMetadata
    {
        public bool SuppressMatching => true;
        public bool ExcludeFromDescription => true;
    }
}
