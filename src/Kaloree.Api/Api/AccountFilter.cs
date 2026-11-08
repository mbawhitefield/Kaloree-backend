using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Api;

public sealed class CurrentAccount
{
    public Account Value { get; set; } = null!;
    public Guid Id => Value.Id;
}

public sealed class AccountFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var h = context.HttpContext;
        var subject = h.User.FindFirst("sub")?.Value;
        Check.That(subject is { Length: > 0 and <= 200 }, "invalid_identity", 401);
        var db = h.RequestServices.GetRequiredService<AppDb>();
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Subject == subject, h.RequestAborted);
        // Explicit provisioning prevents a deleted account from silently reappearing.
        if (account is null && h.Request.Method == "POST" && h.Request.Path == "/api/v1/account")
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Accounts" ("Id", "Subject", "CreatedAt", "ActivityFactor", "ProteinPercent", "FatPercent", "PhotoConsent", "Version")
                VALUES ({id}, {subject!}, {DateTimeOffset.UtcNow}, 1.2, 25, 30, false, 0)
                ON CONFLICT ("Subject") DO NOTHING
                """, h.RequestAborted);
            account = await db.Accounts.SingleAsync(x => x.Subject == subject, h.RequestAborted);
        }
        Check.That(account is not null, "account_not_provisioned", 404);
        var now = DateTimeOffset.UtcNow;
        var quota = h.RequestServices.GetRequiredService<Quota>();
        Check.That(await quota.Take($"api:{account!.Id}:{now:yyyyMMddHHmm}", 120, now.AddMinutes(2), h.RequestAborted), "request_limit", 429);
        h.RequestServices.GetRequiredService<CurrentAccount>().Value = account!;
        h.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }
}
