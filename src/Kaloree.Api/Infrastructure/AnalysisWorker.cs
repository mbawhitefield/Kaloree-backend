using System.Text.Json;
using Kaloree.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Infrastructure;

public sealed class AnalysisWorker(IServiceScopeFactory scopes, ILogger<AnalysisWorker> log, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                if (DateTimeOffset.UtcNow >= nextCleanup)
                { await Cleanup(db, stoppingToken); nextCleanup = DateTimeOffset.UtcNow.AddMinutes(5); }
                if (!config.GetValue<bool>("AI:Enabled")) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                var job = await Claim(db, stoppingToken);
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(80));
                string? result = null, error = null;
                var retry = false;
                try
                {
                    Telemetry.PhotoAttempts.Add(1);
                    // Deleting or revoking consent prevents jobs that have not yet been dispatched.
                    var consent = await db.Accounts.AnyAsync(x => x.Id == job.AccountId && x.PhotoConsent, timeout.Token);
                    if (!consent) throw new VisionException(false, "consent_revoked");
                    var estimate = await scope.ServiceProvider.GetRequiredService<IMealVision>().Analyze(job, timeout.Token);
                    result = JsonSerializer.Serialize(estimate, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Telemetry.PhotoFailures.Add(1);
                    error = ex is VisionException ? ex.Message : "vision_unavailable";
                    retry = ex is VisionException v ? v.Retryable : ex is HttpRequestException or OperationCanceledException;
                    log.LogWarning("Photo analysis attempt failed; code={Code}, retry={Retry}", error, retry);
                }
                var status = result is not null ? "completed" : retry && job.Attempts < 3 ? "queued" : "failed";
                var available = DateTimeOffset.UtcNow.AddSeconds(10 * job.Attempts * job.Attempts);
                var encrypted = status == "queued" ? job.EncryptedPhoto : null;
                // Fencing token: an expired/stolen lease can never overwrite the new worker's result.
                await db.AnalysisJobs.Where(x => x.Id == job.Id && x.LeaseToken == job.LeaseToken)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.ResultJson, result)
                        .SetProperty(x => x.ErrorCode, error).SetProperty(x => x.AvailableAt, available)
                        .SetProperty(x => x.EncryptedPhoto, encrypted).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                        .SetProperty(x => x.LeaseToken, (Guid?)null), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Do not log exception messages: database/provider errors can contain personal data.
                log.LogError("Worker cycle failed: {Type}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public static async Task<AnalysisJob?> Claim(AppDb db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow; var lease = now.AddMinutes(2); var token = Guid.NewGuid();
        // One atomic PostgreSQL statement. No in-memory queue or distributed lock service required.
        var jobs = await db.AnalysisJobs.FromSqlInterpolated($"""
            UPDATE "AnalysisJobs" SET "Status" = 'processing', "Attempts" = "Attempts" + 1,
                "LeaseUntil" = {lease}, "LeaseToken" = {token}
            WHERE "Id" = (
                SELECT "Id" FROM "AnalysisJobs"
                WHERE (("Status" = 'queued' AND "AvailableAt" <= {now})
                    OR ("Status" = 'processing' AND "LeaseUntil" < {now}))
                    AND "Attempts" < 3 AND "CreatedAt" > {now.AddHours(-24)}
                ORDER BY "CreatedAt" FOR UPDATE SKIP LOCKED LIMIT 1
            ) RETURNING *
            """).AsNoTracking().ToListAsync(ct);
        return jobs.SingleOrDefault();
    }

    private static async Task Cleanup(AppDb db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.AnalysisJobs.Where(x => (x.CreatedAt < now.AddHours(-24) ||
            (x.Status == "processing" && x.Attempts >= 3 && x.LeaseUntil < now)) &&
            (x.Status == "queued" || x.Status == "processing"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "failed").SetProperty(x => x.ErrorCode, "analysis_expired")
                .SetProperty(x => x.EncryptedPhoto, (byte[]?)null).SetProperty(x => x.LeaseToken, (Guid?)null), ct);
        await db.AnalysisJobs.Where(x => x.CreatedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        await db.UsageBuckets.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(ct);
        await db.BillingEvents.Where(x => x.ReceivedAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);
    }
}
