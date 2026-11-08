using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kaloree.Api.Api;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Infrastructure;

public static class WebhookSignature
{
    public static bool Verify(string header, byte[] body, string secret, DateTimeOffset now)
    {
        var parts = header.Split(',').Select(x => x.Trim().Split('=', 2)).Where(x => x.Length == 2).ToArray();
        var timestamp = parts.FirstOrDefault(x => x[0] == "t")?.ElementAt(1);
        var signature = parts.FirstOrDefault(x => x[0] == "v1")?.ElementAt(1);
        var current = now.ToUnixTimeSeconds();
        if (!long.TryParse(timestamp, out var seconds) || seconds < current - 300 || seconds > current + 300 || signature?.Length != 64) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); } catch (FormatException) { return false; }
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signed = new byte[prefix.Length + body.Length]; prefix.CopyTo(signed, 0); body.CopyTo(signed, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}

public interface ISubscriptionProvider
{
    Task<DateTimeOffset?> PremiumExpiry(Guid account, CancellationToken ct);
}

public sealed class RevenueCat(HttpClient http, IConfiguration config) : ISubscriptionProvider
{
    public async Task<DateTimeOffset?> PremiumExpiry(Guid account, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.revenuecat.com/v1/subscribers/{account:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Billing:ApiKey"]);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(503, "billing_unavailable");
        await response.Content.LoadIntoBufferAsync(256 * 1024, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var entitlements = json.RootElement.GetProperty("subscriber").GetProperty("entitlements");
        if (!entitlements.TryGetProperty(config["Billing:Entitlement"] ?? "premium", out var entitlement)) return null;
        DateTimeOffset? end = entitlement.TryGetProperty("expires_date", out var expiry) && expiry.ValueKind == JsonValueKind.String ? expiry.GetDateTimeOffset() : null;
        if (entitlement.TryGetProperty("grace_period_expires_date", out var grace) && grace.ValueKind == JsonValueKind.String &&
            (end is null || grace.GetDateTimeOffset() > end)) end = grace.GetDateTimeOffset();
        return end;
    }
}

public static class BillingEndpoint
{
    public static void MapBilling(this WebApplication app)
    {
        app.MapPost("/webhooks/revenuecat", async (HttpRequest request, AppDb db, IConfiguration config,
            ISubscriptionProvider provider, CancellationToken ct) =>
        {
            var secret = config["Billing:WebhookSecret"];
            Check.That(config.GetValue<bool>("Billing:Enabled") && !string.IsNullOrWhiteSpace(secret), "billing_disabled", 503);
            using var ms = new MemoryStream(); await request.Body.CopyToAsync(ms, ct);
            var raw = ms.ToArray(); Check.That(raw.Length <= 65536, "payload_too_large", 413);
            Check.That(WebhookSignature.Verify(request.Headers["X-RevenueCat-Webhook-Signature"].ToString(), raw, secret!, DateTimeOffset.UtcNow), "invalid_signature", 401);
            using var json = JsonDocument.Parse(raw);
            Check.That(json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("event", out _));
            var ev = json.RootElement.GetProperty("event");
            Check.That(ev.ValueKind == JsonValueKind.Object && ev.TryGetProperty("id", out var eid) && eid.ValueKind == JsonValueKind.String &&
                ev.TryGetProperty("type", out var etype) && etype.ValueKind == JsonValueKind.String);
            var id = ev.GetProperty("id").GetString(); Check.Text(id, 200);
            if (ev.GetProperty("type").GetString() == "TEST") return Results.Ok();
            Check.That(ev.TryGetProperty("environment", out var environment) && environment.GetString() == config["Billing:Environment"], "wrong_billing_environment");
            Check.That(ev.TryGetProperty("event_timestamp_ms", out var ts) && ts.TryGetInt64(out _));
            var timestamp = ev.GetProperty("event_timestamp_ms").GetInt64();
            var users = new HashSet<Guid>();
            if (ev.TryGetProperty("app_user_id", out var user) && Guid.TryParse(user.GetString(), out var account)) users.Add(account);
            foreach (var field in new[] { "transferred_from", "transferred_to", "aliases" })
                if (ev.TryGetProperty(field, out var values) && values.ValueKind == JsonValueKind.Array)
                    foreach (var v in values.EnumerateArray()) if (Guid.TryParse(v.GetString(), out var uid)) users.Add(uid);
            Check.That(users.Count is > 0 and <= 20, "unmapped_billing_identity");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "BillingEvents" ("Id", "ReceivedAt") VALUES ({id!}, {DateTimeOffset.UtcNow})
                ON CONFLICT ("Id") DO NOTHING
                """, ct);
            if (inserted == 0) return Results.Ok();
            foreach (var uid in users.Order())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Accounts\" WHERE \"Id\" = {uid} FOR UPDATE", ct);
                if (!await db.Accounts.AnyAsync(x => x.Id == uid, ct)) continue;
                var expiry = await provider.PremiumExpiry(uid, ct);
                var product = config["Billing:Entitlement"] ?? "premium";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Entitlements" ("Id", "AccountId", "Product", "ExpiresAt", "EventTimestamp")
                    VALUES ({Guid.NewGuid()}, {uid}, {product}, {expiry}, {timestamp})
                    ON CONFLICT ("AccountId", "Product") DO UPDATE SET "ExpiresAt" = EXCLUDED."ExpiresAt",
                        "EventTimestamp" = EXCLUDED."EventTimestamp"
                    """, ct);
            }
            await tx.CommitAsync(ct); return Results.Ok();
        }).AllowAnonymous().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(65536));
    }
}
