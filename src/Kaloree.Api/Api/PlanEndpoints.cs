using System.Text.Json;
using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Api;

public sealed record PlanInput(DateOnly Date, string Slot, string Name, ItemInput[] Items, int Version);

public static class PlanEndpoints
{
    public static void MapPlans(this RouteGroupBuilder api)
    {
        var plans = api.MapGroup("/plans").AddEndpointFilter(async (context, next) =>
        {
            var a = context.HttpContext.RequestServices.GetRequiredService<CurrentAccount>();
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
            Check.That(await db.Entitlements.AnyAsync(x => x.AccountId == a.Id && x.ExpiresAt > DateTimeOffset.UtcNow,
                context.HttpContext.RequestAborted), "premium_required", 403);
            return await next(context);
        });
        plans.MapGet("/", async (DateOnly from, DateOnly to, int? page, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Range(from, to, 93); var p = page ?? 1; Check.That(p is >= 1 and <= 1000);
            return Results.Ok(await db.PlannedMeals.AsNoTracking().Where(x => x.AccountId == a.Id && x.Date >= from && x.Date <= to)
                .OrderBy(x => x.Date).ThenBy(x => x.Id).Skip((p - 1) * 50).Take(50).ToListAsync(ct));
        });
        plans.MapPut("/{id:guid}", async (Guid id, PlanInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Check.That(input.Date >= today.AddDays(-366) && input.Date <= today.AddDays(366));
            Check.Text(input.Name); Check.That(input.Slot is "breakfast" or "lunch" or "dinner" or "snack");
            var plan = await db.PlannedMeals.SingleOrDefaultAsync(x => x.Id == id, ct);
            Check.That(plan is null || plan.AccountId == a.Id, "not_found", 404);
            if (plan is null)
            { Check.That(input.Version == 0, "version_conflict", 409); plan = new PlannedMeal { Id = id, AccountId = a.Id }; db.PlannedMeals.Add(plan); }
            else Check.That(plan.Version == input.Version, "version_conflict", 409);
            var items = await FoodEndpoints.Resolve(input.Items, a.Id, db, ct);
            plan.Date = input.Date; plan.Slot = input.Slot; plan.Name = input.Name.Trim(); plan.ItemsJson = JsonSerializer.Serialize(items); plan.Version++;
            await db.SaveChangesAsync(ct); return Results.Ok(plan);
        });
        // Deletion is allowed after subscription expiry through the account-level endpoint.
        api.MapDelete("/plans/{id:guid}", async (Guid id, int version, CurrentAccount a, AppDb db, CancellationToken ct) =>
            await db.PlannedMeals.Where(x => x.Id == id && x.AccountId == a.Id && x.Version == version).ExecuteDeleteAsync(ct) == 1 ? Results.NoContent() : Results.Conflict());
    }
}
