using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Api;

public static class ProfileEndpoints
{
    public static void MapProfile(this RouteGroupBuilder api)
    {
        api.MapPost("/account", (CurrentAccount a) => Results.Ok(new { a.Id, billingAppUserId = a.Id.ToString() }));
        api.MapGet("/profile", (CurrentAccount a) => Results.Ok(a.Value));
        api.MapPut("/profile", async (ProfileInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var age = Nutrition.Age(input.BirthDate, DateOnly.FromDateTime(DateTime.UtcNow));
            Check.That(age is >= 18 and <= 100 && input.EquationSex is "male" or "female");
            Check.That(input.HeightCm is >= 100 and <= 250 && input.WeightKg is >= 30 and <= 400);
            Check.That(new[] { 1.2m, 1.375m, 1.55m, 1.725m, 1.9m }.Contains(input.ActivityFactor));
            Check.That(a.Value.Version == input.Version, "version_conflict", 409);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            a.Value.BirthDate = input.BirthDate; a.Value.EquationSex = input.EquationSex;
            a.Value.HeightCm = input.HeightCm; a.Value.WeightKg = input.WeightKg;
            a.Value.ActivityFactor = input.ActivityFactor; a.Value.PhotoConsent = input.PhotoConsent; a.Value.Version++;
            if (!input.PhotoConsent)
                await db.AnalysisJobs.Where(x => x.AccountId == a.Id && x.Status == "queued")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "failed").SetProperty(x => x.ErrorCode, "consent_revoked")
                        .SetProperty(x => x.EncryptedPhoto, (byte[]?)null), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(a.Value);
        });
        api.MapGet("/calculations", (CurrentAccount a) =>
        {
            var p = a.Value;
            Check.That(p.BirthDate.HasValue && p.HeightCm.HasValue && p.WeightKg.HasValue, "profile_required", 409);
            var bmr = Nutrition.Bmr(p.WeightKg!.Value, p.HeightCm!.Value, Nutrition.Age(p.BirthDate!.Value,
                DateOnly.FromDateTime(DateTime.UtcNow)), p.EquationSex!);
            return Results.Ok(new { bmr, tdee = Nutrition.Tdee(bmr, p.ActivityFactor), method = "Mifflin-St Jeor", estimated = true });
        });
        api.MapPut("/goals", async (GoalInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.That(input.Calories is >= 1200 and <= 6000 && input.ProteinPercent is >= 10 and <= 50 &&
                       input.FatPercent is >= 15 and <= 50 && input.ProteinPercent + input.FatPercent <= 80);
            Check.That(a.Value.Version == input.Version, "version_conflict", 409);
            a.Value.CalorieTarget = input.Calories; a.Value.ProteinPercent = input.ProteinPercent;
            a.Value.FatPercent = input.FatPercent; a.Value.Version++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { input.Calories, proteinGrams = input.Calories * input.ProteinPercent / 400m,
                fatGrams = input.Calories * input.FatPercent / 900m,
                carbsGrams = input.Calories * (100 - input.ProteinPercent - input.FatPercent) / 400m, a.Value.Version });
        });
        api.MapGet("/subscription", async (CurrentAccount a, AppDb db, CancellationToken ct) =>
            Results.Ok(new { premium = await db.Entitlements.AnyAsync(x => x.AccountId == a.Id &&
                x.ExpiresAt > DateTimeOffset.UtcNow, ct), photoScansPerUtcMonth = 100 }));
        api.MapGet("/export", async (int? page, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var p = page ?? 1; Check.That(p is >= 1 and <= 100000);
            var skip = (p - 1) * 500;
            return Results.Ok(new {
            page = p, pageSize = 500,
            profile = a.Value,
            meals = await db.Meals.AsNoTracking().Include(x => x.Items).Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            weights = await db.Weights.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            water = await db.Water.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            foods = await db.Foods.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            recipes = await db.Recipes.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            plans = await db.PlannedMeals.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500).ToListAsync(ct),
            analyses = await db.AnalysisJobs.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Id).Skip(skip).Take(500)
                .Select(x => new { x.Id, x.Status, x.ResultJson, x.ErrorCode, x.CreatedAt }).ToListAsync(ct)
        }); });
        api.MapDelete("/account", async (HttpRequest request, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.That(request.Headers["X-Confirm-Delete"] == "delete-my-account", "confirmation_required");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.UsageBuckets.Where(x => x.Key.StartsWith("scan:" + a.Id + ":")).ExecuteDeleteAsync(ct);
            await db.UsageBuckets.Where(x => x.Key.StartsWith("api:" + a.Id + ":")).ExecuteDeleteAsync(ct);
            await db.Accounts.Where(x => x.Id == a.Id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
    }
}
