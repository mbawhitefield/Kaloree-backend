using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Kaloree.Api.Api;

public static class DiaryEndpoints
{
    public static void MapDiary(this RouteGroupBuilder api)
    {
        api.MapGet("/meals", async (DateOnly from, DateOnly to, int? page, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Range(from, to, 31); var p = page ?? 1; Check.That(p is >= 1 and <= 1000);
            return Results.Ok(await db.Meals.AsNoTracking().Include(x => x.Items)
                .Where(x => x.AccountId == a.Id && x.Date >= from && x.Date <= to)
                .OrderBy(x => x.Date).ThenBy(x => x.Id).Skip((p - 1) * 50).Take(50).ToListAsync(ct));
        });
        api.MapPut("/meals/{id:guid}", async (Guid id, MealInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Date(input.Date); Check.Text(input.Name);
            Check.That(input.Slot is "breakfast" or "lunch" or "dinner" or "snack");
            Check.That(input.Source is "manual" or "photo_confirmed" or "recipe" or "barcode");
            var meal = await db.Meals.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
            Check.That(meal is null || meal.AccountId == a.Id, "not_found", 404);
            if (meal is null)
            {
                Check.That(input.Version == 0, "version_conflict", 409);
                meal = new Meal { Id = id, AccountId = a.Id }; db.Meals.Add(meal);
            }
            else Check.That(meal.Version == input.Version, "version_conflict", 409);
            var items = await FoodEndpoints.Resolve(input.Items, a.Id, db, ct);
            meal.Date = input.Date; meal.Name = input.Name.Trim(); meal.Slot = input.Slot; meal.Source = input.Source;
            db.RemoveRange(meal.Items); meal.Items = items; meal.Version++; meal.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); return Results.Ok(meal);
        });
        api.MapDelete("/meals/{id:guid}", async (Guid id, int version, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var count = await db.Meals.Where(x => x.Id == id && x.AccountId == a.Id && x.Version == version).ExecuteDeleteAsync(ct);
            return count == 1 ? Results.NoContent() : Results.Conflict(new { code = "missing_or_version_conflict" });
        });
        api.MapPut("/weights/{date}", async (DateOnly date, WeightInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Date(date); Check.That(input.Date == date && input.Kg is >= 30 and <= 400);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Weights" ("Id", "AccountId", "Date", "Kg") VALUES ({Guid.NewGuid()}, {a.Id}, {date}, {input.Kg})
                ON CONFLICT ("AccountId", "Date") DO UPDATE SET "Kg" = EXCLUDED."Kg"
                """, ct);
            return Results.NoContent();
        });
        api.MapGet("/weights", async (DateOnly from, DateOnly to, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Range(from, to); return Results.Ok(await db.Weights.AsNoTracking().Where(x => x.AccountId == a.Id && x.Date >= from && x.Date <= to).OrderBy(x => x.Date).ToListAsync(ct));
        });
        api.MapDelete("/weights/{date}", async (DateOnly date, CurrentAccount a, AppDb db, CancellationToken ct) =>
        { await db.Weights.Where(x => x.AccountId == a.Id && x.Date == date).ExecuteDeleteAsync(ct); return Results.NoContent(); });
        api.MapPut("/water/{id:guid}", async (Guid id, WaterInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Date(input.Date); Check.That(input.Millilitres is > 0 and <= 5000);
            var entry = await db.Water.SingleOrDefaultAsync(x => x.Id == id, ct);
            Check.That(entry is null || entry.AccountId == a.Id, "not_found", 404);
            if (entry is not null)
            {
                Check.That(entry.Date == input.Date && entry.Millilitres == input.Millilitres, "immutable_id_reused", 409);
                return Results.Ok(entry);
            }
            entry = new WaterEntry { Id = id, AccountId = a.Id, Date = input.Date, Millilitres = input.Millilitres };
            db.Water.Add(entry); await db.SaveChangesAsync(ct); return Results.Ok(entry);
        });
        api.MapGet("/water", async (DateOnly date, CurrentAccount a, AppDb db, CancellationToken ct) =>
            Results.Ok(await db.Water.AsNoTracking().Where(x => x.AccountId == a.Id && x.Date == date).ToListAsync(ct)));
        api.MapDelete("/water/{id:guid}", async (Guid id, CurrentAccount a, AppDb db, CancellationToken ct) =>
        { await db.Water.Where(x => x.Id == id && x.AccountId == a.Id).ExecuteDeleteAsync(ct); return Results.NoContent(); });
        api.MapGet("/summary", async (DateOnly from, DateOnly to, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Range(from, to);
            var totals = await db.Meals.AsNoTracking().Where(m => m.AccountId == a.Id && m.Date >= from && m.Date <= to)
                .SelectMany(m => m.Items.Select(i => new { m.Date, i.Calories, i.Protein, i.Carbs, i.Fat }))
                .GroupBy(x => x.Date).Select(g => new { date = g.Key, calories = g.Sum(x => x.Calories),
                    protein = g.Sum(x => x.Protein), carbs = g.Sum(x => x.Carbs), fat = g.Sum(x => x.Fat) }).OrderBy(x => x.date).ToListAsync(ct);
            return Results.Ok(new { a.Value.CalorieTarget, days = totals });
        });
        api.MapGet("/recipes", async (int? page, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            var p = page ?? 1; Check.That(p is >= 1 and <= 1000);
            return Results.Ok(await db.Recipes.AsNoTracking().Where(x => x.AccountId == a.Id).OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * 30).Take(30).ToListAsync(ct));
        });
        api.MapPut("/recipes/{id:guid}", async (Guid id, RecipeInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Text(input.Name); Check.Items(input.Items); Check.That(input.Servings is >= 1 and <= 100);
            var recipe = await db.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct);
            Check.That(recipe is null || recipe.AccountId == a.Id, "not_found", 404);
            if (recipe is null) { Check.That(input.Version == 0, "version_conflict", 409); recipe = new Recipe { Id = id, AccountId = a.Id }; db.Recipes.Add(recipe); }
            else Check.That(recipe.Version == input.Version, "version_conflict", 409);
            var items = await FoodEndpoints.Resolve(input.Items, a.Id, db, ct);
            recipe.Name = input.Name.Trim(); recipe.Servings = input.Servings;
            recipe.ItemsJson = JsonSerializer.Serialize(items); recipe.Version++;
            await db.SaveChangesAsync(ct); return Results.Ok(recipe);
        });
        api.MapDelete("/recipes/{id:guid}", async (Guid id, int version, CurrentAccount a, AppDb db, CancellationToken ct) =>
            await db.Recipes.Where(x => x.Id == id && x.AccountId == a.Id && x.Version == version).ExecuteDeleteAsync(ct) == 1 ? Results.NoContent() : Results.Conflict());
    }
}
