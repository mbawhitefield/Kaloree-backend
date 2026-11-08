using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Infrastructure;

public sealed class Quota(AppDb db)
{
    // Called inside the same transaction as the protected write. Atomic across all replicas.
    public async Task<bool> Take(string key, int limit, DateTimeOffset expires, CancellationToken ct)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UsageBuckets" ("Key", "Count", "ExpiresAt") VALUES ({key}, 1, {expires})
            ON CONFLICT ("Key") DO UPDATE SET "Count" = "UsageBuckets"."Count" + 1
            WHERE "UsageBuckets"."Count" < {limit}
            """, ct);
        return affected == 1;
    }
}
