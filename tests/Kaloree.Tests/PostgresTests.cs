using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Kaloree.Api.Api;
using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Kaloree.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_DATABASE")))
            Skip = "Set TEST_DATABASE to an isolated PostgreSQL admin connection; see README.";
    }
}

public sealed class ApiFixture : IAsyncLifetime
{
    public const string SigningKey = "local-test-signing-key-with-64-characters-do-not-use-outside-tests-123456";
    public const string WebhookSecret = "local-webhook-secret-not-for-production-123456789";
    public string Connection { get; private set; } = "";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private string database = "";
    public async Task InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("TEST_DATABASE");
        if (string.IsNullOrEmpty(admin)) return;
        database = "kaloree_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin); await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", connection); await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(admin) { Database = database, MaxPoolSize = 30 }; Connection = cs.ConnectionString;
        await using var db = Db(); await db.Database.MigrateAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development"); b.UseSetting("ConnectionStrings:Database", Connection);
            b.UseSetting("Auth:DevelopmentTokens", "true"); b.UseSetting("Auth:DevelopmentSigningKey", SigningKey);
            b.UseSetting("AI:Enabled", "true"); b.UseSetting("AI:ApiKey", "fake"); b.UseSetting("AI:Model", "fake");
            b.UseSetting("AI:EncryptionKey", Convert.ToBase64String(new byte[32]));
            b.UseSetting("AI:DailyGlobalLimit", "1000"); b.UseSetting("AI:MonthlyScanLimit", "100");
            b.UseSetting("Billing:Enabled", "true"); b.UseSetting("Billing:ApiKey", "fake");
            b.UseSetting("Billing:WebhookSecret", WebhookSecret); b.UseSetting("Billing:Environment", "PRODUCTION");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Scoped<ISubscriptionProvider, FakeSubscription>()));
        });
    }
    public AppDb Db() => new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(Connection).Options);
    public HttpClient Client(string? subject = null, bool expired = false, string audience = "kaloree-api", string? signingKey = null)
    {
        var client = Factory.CreateClient();
        if (subject is not null)
        {
            var jwt = new JwtSecurityToken("kaloree-local", audience, [new Claim("sub", subject)],
                DateTime.UtcNow.AddHours(-2), expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        }
        return client;
    }
    public async Task<(HttpClient Client, Guid Id)> Account(bool premium = false)
    {
        var subject = Guid.NewGuid().ToString(); var client = Client(subject);
        var response = await client.PostAsync("/api/v1/account", null); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var db = Db(); var account = await db.Accounts.SingleAsync(x => x.Id == id); account.PhotoConsent = true;
        if (premium) db.Entitlements.Add(new Entitlement { AccountId = id, Product = "premium", ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
        await db.SaveChangesAsync(); return (client, id);
    }
    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(database)) return;
        await Factory.DisposeAsync(); NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("TEST_DATABASE")); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
    private sealed class FakeSubscription : ISubscriptionProvider
    { public Task<DateTimeOffset?> PremiumExpiry(Guid account, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow.AddDays(30)); }
}

public sealed class PostgresTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [PostgresFact]
    public async Task OpenApiDescribesAuthenticationAndPrivateErrorsAreNotCached()
    {
        using var c = fixture.Client();
        var spec = await c.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.Equal("Kaloree API", spec.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("bearer", spec.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer").GetProperty("scheme").GetString());
        var denied = await c.GetAsync("/api/v1/profile");
        Assert.True(denied.Headers.CacheControl?.NoStore);
    }

    [PostgresFact]
    public async Task CatalogImportsAreRepeatableAndCannotOverwritePrivateFoods()
    {
        var (_, owner) = await fixture.Account(); var id = Guid.NewGuid(); var path = Path.GetTempFileName();
        try
        {
            var row = new CatalogRow(id, "Test source", new FoodInput("Test rice", null, 130, 3, 28, 0));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] { row }));
            await using var db = fixture.Db();
            await CatalogImport.Run(db, path, default); await CatalogImport.Run(db, path, default);
            Assert.Equal(1, await db.Foods.CountAsync(x => x.Id == id && x.AccountId == null));
            var privateId = Guid.NewGuid();
            db.Foods.Add(new Food { Id = privateId, AccountId = owner, Name = "Private" }); await db.SaveChangesAsync();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] { row with { Id = privateId } }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImport.Run(db, path, default));
        }
        finally { File.Delete(path); }
    }

    [PostgresFact]
    public async Task ProfilesValidateAdultsAndComputeTargets()
    {
        var (client, _) = await fixture.Account(); using var c = client;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = new ProfileInput(today.AddYears(-30), "male", 180, 80, 1.55m, true, 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/profile", profile with { BirthDate = today.AddYears(-10) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/v1/profile", profile)).StatusCode);
        var bmr = await c.GetFromJsonAsync<JsonElement>("/api/v1/calculations");
        Assert.Equal(1780, bmr.GetProperty("bmr").GetDecimal());
        Assert.Equal(2759, bmr.GetProperty("tdee").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/v1/goals", new GoalInput(2000, 25, 30, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/v1/goals", new GoalInput(2000, 25, 30, 1))).StatusCode);
    }

    [PostgresFact]
    public async Task FoodSnapshotsAreCalculatedAndPrivateReferencesRejected()
    {
        var (client, _) = await fixture.Account(); var (other, _) = await fixture.Account(); using var c = client; using var o = other;
        var foodId = Guid.NewGuid();
        var food = new FoodInput("Cooked rice", null, 130, 3, 28, 0);
        Assert.Equal(HttpStatusCode.Created, (await c.PutAsJsonAsync($"/api/v1/foods/{foodId}", food)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/v1/foods/{foodId}", food with { Calories100g = 200 })).StatusCode);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var meal = new MealInput(date, "lunch", "Rice", "manual", [new(foodId, "Rice", 150, 0, 0, 0, 0)], 0);
        Assert.Equal(HttpStatusCode.NotFound, (await o.PutAsJsonAsync($"/api/v1/meals/{Guid.NewGuid()}", meal)).StatusCode);
        var saved = await c.PutAsJsonAsync($"/api/v1/meals/{Guid.NewGuid()}", meal); saved.EnsureSuccessStatusCode();
        var json = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(195, json.GetProperty("items")[0].GetProperty("calories").GetDecimal());
        await c.DeleteAsync($"/api/v1/foods/{foodId}");
        var summary = await c.GetFromJsonAsync<JsonElement>($"/api/v1/summary?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        Assert.Equal(195, summary.GetProperty("days")[0].GetProperty("calories").GetDecimal());
        var export = await c.GetFromJsonAsync<JsonElement>("/api/v1/export?page=1");
        Assert.Equal(1, export.GetProperty("meals").GetArrayLength());
    }

    [PostgresFact]
    public async Task PlansRequirePremiumButRemainDeletableAfterExpiry()
    {
        var (client, owner) = await fixture.Account(true); using var c = client;
        var id = Guid.NewGuid(); var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var plan = new PlanInput(date, "lunch", "Rice", [new(null, "Rice", 100, 130, 3, 28, 0)], 0);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/v1/plans/{id}", plan)).StatusCode);
        await using var db = fixture.Db();
        await db.Entitlements.Where(x => x.AccountId == owner).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/v1/plans/{Guid.NewGuid()}", plan)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/plans/{id}?version=1")).StatusCode);
    }

    [PostgresFact]
    public async Task AuthenticationRejectsMissingExpiredWrongAudienceAndWrongSignature()
    {
        using var anonymous = fixture.Client();
        using var expired = fixture.Client("expired", expired: true);
        using var audience = fixture.Client("audience", audience: "another-app");
        using var forged = fixture.Client("forged", signingKey: new string('z', 80));
        foreach (var client in new[] { anonymous, expired, audience, forged })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
    }

    [PostgresFact]
    public async Task MealsAreIsolatedAndVersioned()
    {
        var (alice, _) = await fixture.Account(); var (bob, _) = await fixture.Account();
        using var a = alice; using var b = bob; var id = Guid.NewGuid(); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var input = new MealInput(today, "lunch", "Rice", "manual", [new(null, "Rice", 100, 130, 3, 28, 0)], 0);
        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync($"/api/v1/meals/{id}", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/v1/meals/{id}", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PutAsJsonAsync($"/api/v1/meals/{id}", input)).StatusCode);
        var rows = await b.GetFromJsonAsync<JsonElement>($"/api/v1/meals?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(0, rows.GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await b.DeleteAsync($"/api/v1/meals/{id}?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/v1/meals/{id}?version=1")).StatusCode);
    }

    [PostgresFact]
    public async Task QuotaIsAtomicUnderContention()
    {
        var key = "test:" + Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
        {
            await using var db = fixture.Db(); return await new Quota(db).Take(key, 10, DateTimeOffset.UtcNow.AddDays(1), default);
        }));
        Assert.Equal(10, results.Count(x => x));
    }

    [PostgresFact]
    public async Task PhotoRequestsAreIdempotentAndPremiumOnly()
    {
        var (free, _) = await fixture.Account(); var (paid, owner) = await fixture.Account(true);
        using var f = free; using var p = paid;
        var photo = new PhotoInput("image/jpeg", Convert.ToBase64String(new byte[] { 255, 216, 255, 0, 1 }), true);
        f.DefaultRequestHeaders.Add("Idempotency-Key", "free");
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PostAsJsonAsync("/api/v1/analyses", photo)).StatusCode);
        p.DefaultRequestHeaders.Add("Idempotency-Key", "same-request");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => p.PostAsJsonAsync("/api/v1/analyses", photo)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        await using var db = fixture.Db();
        Assert.Equal(1, await db.AnalysisJobs.CountAsync(x => x.AccountId == owner));
        Assert.Equal(1, (await db.UsageBuckets.SingleAsync(x => x.Key.StartsWith("scan:" + owner))).Count);
        var changed = photo with { Base64 = Convert.ToBase64String(new byte[] { 255, 216, 255, 2 }) };
        Assert.Equal(HttpStatusCode.Conflict, (await p.PostAsJsonAsync("/api/v1/analyses", changed)).StatusCode);
    }

    [PostgresFact]
    public async Task WorkerLeaseHasSingleOwnerAndFencesStaleWrites()
    {
        var (_, owner) = await fixture.Account(); var id = Guid.NewGuid();
        await using (var db = fixture.Db())
        {
            db.AnalysisJobs.Add(new AnalysisJob { Id = id, AccountId = owner, IdempotencyKey = id.ToString(), RequestHash = "hash" });
            await db.SaveChangesAsync();
        }
        var claimed = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        { await using var db = fixture.Db(); return await AnalysisWorker.Claim(db, default); }));
        var job = Assert.Single(claimed, x => x?.Id == id)!;
        await using var verify = fixture.Db();
        await verify.AnalysisJobs.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var replacement = await AnalysisWorker.Claim(verify, default); Assert.NotNull(replacement);
        var stale = await verify.AnalysisJobs.Where(x => x.Id == id && x.LeaseToken == job.LeaseToken)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "completed"));
        Assert.Equal(0, stale);
        Assert.NotEqual(job.LeaseToken, replacement.LeaseToken);
    }

    [PostgresFact]
    public async Task BillingWebhooksRejectTamperingAndDeduplicate()
    {
        var (client, owner) = await fixture.Account(); using var c = client;
        var id = Guid.NewGuid().ToString();
        var raw = JsonSerializer.SerializeToUtf8Bytes(new { @event = new { id, type = "RENEWAL", environment = "PRODUCTION", app_user_id = owner.ToString(), event_timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } });
        async Task<HttpResponseMessage> Send(string signature)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/revenuecat") { Content = new ByteArrayContent(raw) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Add("X-RevenueCat-Webhook-Signature", signature); return await c.SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send("invalid")).StatusCode);
        var valid = SecurityTests.Sign(raw, ApiFixture.WebhookSecret, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await Send(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(valid)).StatusCode);
        await using var db = fixture.Db(); Assert.Equal(1, await db.BillingEvents.CountAsync(x => x.Id == id));
        Assert.True(await db.Entitlements.AnyAsync(x => x.AccountId == owner && x.ExpiresAt > DateTimeOffset.UtcNow));
    }

    [PostgresFact]
    public async Task AccountDeletionCascadesPrivateData()
    {
        var (client, owner) = await fixture.Account(true); using var c = client;
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync("/api/v1/account")).StatusCode);
        c.DefaultRequestHeaders.Add("X-Confirm-Delete", "delete-my-account");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/profile")).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.Entitlements.AnyAsync(x => x.AccountId == owner));
    }
}
