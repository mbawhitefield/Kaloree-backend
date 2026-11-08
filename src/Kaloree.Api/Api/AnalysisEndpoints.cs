using System.Security.Cryptography;
using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Api;

public static class AnalysisEndpoints
{
    public static void MapAnalysis(this RouteGroupBuilder api)
    {
        api.MapPost("/analyses", async (PhotoInput input, HttpRequest request, CurrentAccount a, AppDb db,
            Quota quota, PhotoCipher cipher, IConfiguration config, CancellationToken ct) =>
        {
            Check.That(config.GetValue<bool>("AI:Enabled"), "photo_analysis_disabled", 503);
            var key = request.Headers["Idempotency-Key"].ToString(); Check.Text(key, 100);
            Check.That(input.Consent && a.Value.PhotoConsent, "photo_consent_required", 403);
            Check.That(input.Base64 is { Length: > 0 and <= 7000000 });
            byte[] photo;
            try { photo = Convert.FromBase64String(input.Base64); }
            catch (FormatException) { throw new ApiException(400, "invalid_photo"); }
            Check.That(photo.Length <= 5 * 1024 * 1024 && IsImage(photo, input.MimeType), "invalid_photo");
            var hash = Convert.ToHexString(SHA256.HashData(photo));
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serialize creates for this account before checking idempotency and charging quota.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Accounts\" WHERE \"Id\" = {a.Id} FOR UPDATE", ct);
            var existing = await db.AnalysisJobs.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == a.Id && x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                Check.That(existing.RequestHash == hash && existing.MimeType == input.MimeType, "idempotency_key_reused", 409);
                return Results.Accepted($"/api/v1/analyses/{existing.Id}", View(existing));
            }
            Check.That(await db.Entitlements.AnyAsync(x => x.AccountId == a.Id && x.ExpiresAt > DateTimeOffset.UtcNow, ct), "premium_required", 403);
            var now = DateTimeOffset.UtcNow;
            var monthEnd = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            Check.That(await quota.Take($"scan:{a.Id}:{now:yyyyMM}", config.GetValue("AI:MonthlyScanLimit", 100), monthEnd.AddDays(1), ct), "monthly_scan_limit", 429);
            var dayEnd = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            Check.That(await quota.Take($"ai-global:{now:yyyyMMdd}", config.GetValue("AI:DailyGlobalLimit", 1000), dayEnd.AddDays(1), ct), "daily_service_limit", 429);
            var job = new AnalysisJob { AccountId = a.Id, IdempotencyKey = key, RequestHash = hash,
                MimeType = input.MimeType, EncryptedPhoto = cipher.Encrypt(photo, a.Id) };
            CryptographicOperations.ZeroMemory(photo);
            db.AnalysisJobs.Add(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Accepted($"/api/v1/analyses/{job.Id}", View(job));
        });
        api.MapGet("/analyses/{id:guid}", async (Guid id, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var job = await db.AnalysisJobs.AsNoTracking().Where(x => x.Id == id && x.AccountId == a.Id)
                .Select(x => new { x.Id, x.Status, x.ResultJson, x.ErrorCode, x.CreatedAt }).SingleOrDefaultAsync(ct);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });
        api.MapDelete("/analyses/{id:guid}", async (Guid id, CurrentAccount a, AppDb db, CancellationToken ct) =>
        { await db.AnalysisJobs.Where(x => x.Id == id && x.AccountId == a.Id).ExecuteDeleteAsync(ct); return Results.NoContent(); });
    }

    public static bool IsImage(byte[] bytes, string mime) => mime switch
    {
        "image/jpeg" => bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        "image/png" => bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/webp" => bytes.Length > 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private static object View(AnalysisJob job) => new { job.Id, job.Status, job.CreatedAt };
}
