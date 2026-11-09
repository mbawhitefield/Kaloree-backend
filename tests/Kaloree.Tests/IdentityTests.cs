using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kaloree.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;

namespace Kaloree.Tests;

public sealed class IdentityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Password = "Test-password-12345!";
    private const string NewPassword = "Changed-password-67890!";
    private readonly IPAddress clientAddress = new(new byte[] { 10 }.Concat(Guid.NewGuid().ToByteArray().Take(3)).ToArray());

    private WebApplicationFactory<Program> Factory(bool swagger = true, string role = "api") => fixture.Factory.WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Production");
        b.UseSetting("Auth:Mode", "local");
        b.UseSetting("Auth:DevelopmentTokens", "false");
        b.UseSetting("Swagger:Enabled", swagger.ToString());
        b.UseSetting("Service:Role", role);
        b.UseSetting("AI:Enabled", "false");
        b.UseSetting("Billing:Enabled", "false");
        b.ConfigureServices(s =>
        {
            s.AddSingleton<IStartupFilter>(new ClientIp(clientAddress));
        });
    });

    private async Task<string> Register(HttpClient client)
    {
        var address = Guid.NewGuid().ToString("N") + "@kaloree.test";
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = address, password = Password })).EnsureSuccessStatusCode();
        return address;
    }

    private static async Task<JsonElement> Login(HttpClient client, string address, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void Authorize(HttpClient client, JsonElement tokens) => client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());

    [PostgresFact]
    public async Task RegistrationLoginProvisioningAndDeletionPreserveOwnership()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        var address = await Register(c);
        var tokens = await Login(c, address);
        Assert.Equal("Bearer", tokens.GetProperty("tokenType").GetString());
        Authorize(c, tokens);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/profile")).StatusCode);
        var provision = await c.PostAsync("/api/v1/account", null);
        provision.EnsureSuccessStatusCode();
        var id = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var profile = await c.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal(id, profile.GetProperty("id").GetGuid());
        Assert.StartsWith("local:", profile.GetProperty("subject").GetString());
        using var other = factory.CreateClient();
        Authorize(other, await Login(other, await Register(other)));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/profile")).StatusCode);

        c.DefaultRequestHeaders.Add("X-Confirm-Delete", "delete-my-account");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() })).StatusCode);
        await using var db = fixture.Db();
        Assert.False(await db.Users.AnyAsync(x => x.Email == address));
        Assert.False(await db.Accounts.AnyAsync(x => x.Id == id));
    }

    [PostgresFact]
    public async Task RefreshSurvivesRestartAndLogoutRevokesAccessAndRefresh()
    {
        await using var first = Factory();
        using var c = first.CreateClient();
        var address = await Register(c);
        var tokens = await Login(c, address);
        await using var second = Factory();
        using var restarted = second.CreateClient();
        var refresh = await restarted.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        refresh.EnsureSuccessStatusCode();
        var renewed = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        Authorize(restarted, renewed);
        (await restarted.GetAsync("/api/v1/auth/manage/info")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await restarted.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.GetAsync("/api/v1/auth/manage/info")).StatusCode);
        restarted.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = renewed.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [PostgresFact]
    public async Task PasswordChangeRequiresCurrentPasswordAndRevokesExistingTokens()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        var address = await Register(c);
        var tokens = await Login(c, address);
        Authorize(c, tokens);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/manage/info", new { oldPassword = "wrong", newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/manage/info", new { newEmail = "other@kaloree.test" })).StatusCode);
        (await c.PostAsJsonAsync("/api/v1/auth/manage/info", new { oldPassword = Password, newPassword = NewPassword })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/manage/info")).StatusCode);
        c.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password = Password })).StatusCode);
        await Login(c, address, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [PostgresFact]
    public async Task PasswordPolicyLockoutAndBearerOnlyLoginAreEnforced()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/register", new { email = "weak@kaloree.test", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "weak@kaloree.test", password = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "weak@kaloree.test", password = new string('A', 1025) })).StatusCode);
        var address = await Register(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/auth/login?useCookies=True", new { email = address, password = Password })).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password = Password })).StatusCode);
        await using var db = fixture.Db();
        var user = await db.Users.SingleAsync(x => x.Email == address);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.NotEqual(Password, user.PasswordHash);
    }

    [PostgresFact]
    public async Task TamperedExpiredAndInvalidRefreshTokensAreRejected()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        var address = await Register(c);
        var tokens = await Login(c, address);
        var access = tokens.GetProperty("accessToken").GetString()!;
        var replacement = access[20] == 'A' ? 'B' : 'A';
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access[..20] + replacement + access[21..]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(address);
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<IdentityUser>>().CreateUserPrincipalAsync(user!);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var expired = options.BearerTokenProtector.Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }, IdentityConstants.BearerScheme));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        c.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "invalid" })).StatusCode);
    }

    [PostgresFact]
    public async Task AuthenticatorRequiresCodeAndRecoveryCodesAreSingleUse()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        var address = await Register(c);
        Authorize(c, await Login(c, address));
        var setup = await c.PostAsJsonAsync("/api/v1/auth/manage/2fa", new { });
        setup.EnsureSuccessStatusCode();
        var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;
        c.DefaultRequestHeaders.Authorization = null;
        var beforeEnable = await Login(c, address);
        Authorize(c, beforeEnable);
        var enable = await c.PostAsJsonAsync("/api/v1/auth/manage/2fa", new { enable = true, twoFactorCode = AuthenticatorCode(key) });
        enable.EnsureSuccessStatusCode();
        var recoveryCode = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")[0].GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/manage/info")).StatusCode);
        c.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password = Password })).StatusCode);
        (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = address, password = Password, twoFactorCode = AuthenticatorCode(key) })).EnsureSuccessStatusCode();
        var recovery = new { email = address, password = Password, twoFactorRecoveryCode = recoveryCode };
        (await c.PostAsJsonAsync("/api/v1/auth/login", recovery)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", recovery)).StatusCode);
    }

    // Generate a real RFC 6238 authenticator code independently of Identity's implementation.
    private static string AuthenticatorCode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0; var bits = 0;
        foreach (var ch in base32.ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(ch); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    [PostgresFact]
    public async Task AuthenticationLimitIsSharedAcrossApiReplicas()
    {
        await using var first = Factory();
        await using var second = Factory();
        using var a = first.CreateClient();
        using var b = second.CreateClient();
        // Seed the distributed bucket near its limit so this check cannot cross a minute boundary.
        await using var db = fixture.Db();
        var now = DateTimeOffset.UtcNow;
        foreach (var minute in new[] { now, now.AddMinutes(1) })
            db.UsageBuckets.Add(new Kaloree.Api.Domain.UsageBucket
            { Key = $"auth:{clientAddress}:{minute:yyyyMMddHHmm}", Count = 19, ExpiresAt = now.AddMinutes(3) });
        await db.SaveChangesAsync();
        var login = new { email = "unknown@kaloree.test", password = Password };
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/v1/auth/login", login)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await b.PostAsJsonAsync("/api/v1/auth/login", login)).StatusCode);
    }

    [PostgresFact]
    public async Task RegistrationWorksWithoutEmailAndEmailRoutesAreAbsent()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        (await c.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        var address = await Register(c);
        await Login(c, address);
        await using var db = fixture.Db();
        Assert.False((await db.Users.SingleAsync(x => x.Email == address)).EmailConfirmed);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/auth/confirmEmail?userId=missing&code=anything")).StatusCode);
        foreach (var route in new[] { "resendConfirmationEmail", "forgotPassword", "resetPassword" })
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/v1/auth/" + route, new { email = address })).StatusCode);
    }

    [PostgresFact]
    public async Task SwaggerWorksInProductionWithLocalAssetsAndProtectedApiOperations()
    {
        await using var factory = Factory();
        using var c = factory.CreateClient();
        var page = await c.GetAsync("/swagger/index.html");
        page.EnsureSuccessStatusCode();
        Assert.Contains("swagger-ui", await page.Content.ReadAsStringAsync());
        Assert.Contains("script-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single());
        (await c.GetAsync("/swagger/swagger-ui-bundle.js")).EnsureSuccessStatusCode();
        var spec = await c.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = spec.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/register", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/confirmEmail", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/forgotPassword", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/resetPassword", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/resendConfirmationEmail", out _));
        Assert.DoesNotContain("\"newEmail\"", spec.GetRawText());
        Assert.False(paths.GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/v1/profile").GetProperty("get").TryGetProperty("security", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
    }

    [PostgresFact]
    public async Task SwaggerCanBeDisabledAndIsNotExposedByWorker()
    {
        await using var disabled = Factory(false);
        using var c = disabled.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/openapi/v1.json")).StatusCode);
        await using var worker = Factory(role: "worker");
        using var w = worker.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await w.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.GetAsync("/openapi/v1.json")).StatusCode);
    }

    private sealed class ClientIp(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
