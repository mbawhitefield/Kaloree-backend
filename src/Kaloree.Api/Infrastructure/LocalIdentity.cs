using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;

namespace Kaloree.Api.Infrastructure;

public static class LocalIdentity
{
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
                    ResetPasswordRequest r => string.IsNullOrEmpty(r.Email) || r.Email.Length > 256 ||
                        string.IsNullOrEmpty(r.ResetCode) || r.ResetCode.Length > 16384 || string.IsNullOrEmpty(r.NewPassword),
                    ForgotPasswordRequest r => string.IsNullOrEmpty(r.Email) || r.Email.Length > 256,
                    ResendConfirmationEmailRequest r => string.IsNullOrEmpty(r.Email) || r.Email.Length > 256,
                    InfoRequest r => r.OldPassword?.Length > 1024 || r.NewEmail?.Length > 256,
                    _ => false
                };
                if (invalid) return Results.Problem(statusCode: 400, title: "invalid_request");
                var password = argument switch
                {
                    RegisterRequest r => r.Password,
                    LoginRequest r => r.Password,
                    ResetPasswordRequest r => r.NewPassword,
                    InfoRequest r => r.NewPassword,
                    _ => null
                };
                if (password?.Length > 1024) return Results.Problem(statusCode: 400, title: "password_too_long");
            }
            return await next(context);
        });
        group.MapIdentityApi<IdentityUser>();
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
            var path = h.Request.Path.Value?.ToLowerInvariant();
            if (path is "/api/v1/auth/register" or "/api/v1/auth/resendconfirmationemail" or "/api/v1/auth/forgotpassword" ||
                (path == "/api/v1/auth/manage/info" && h.Request.Method == "POST"))
            {
                // Fail before account creation if email delivery has not been configured.
                if (!h.RequestServices.GetRequiredService<IdentityEmail>().Configured)
                {
                    await Results.Problem(statusCode: 503, title: "email_not_configured").ExecuteAsync(h);
                    return;
                }
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
}
