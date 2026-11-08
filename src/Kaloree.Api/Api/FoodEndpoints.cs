using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Api;

public static class FoodEndpoints
{
    public static void MapFoods(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/barcodes/{barcode}", async (string barcode, FoodCatalog catalog, CancellationToken ct) =>
        {
            var food = await catalog.Barcode(barcode, ct);
            return food is null ? Results.NotFound() : Results.Ok(food);
        });
        api.MapGet("/foods", async (string? q, string? barcode, int? page, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.That(q is null || q.Length <= 100); Check.That(barcode is null || barcode.Length <= 32);
            var p = page ?? 1; Check.That(p is >= 1 and <= 1000);
            var query = db.Foods.AsNoTracking().Where(x => x.AccountId == null || x.AccountId == a.Id);
            if (!string.IsNullOrWhiteSpace(q))
            {
                Check.That(q.Trim().Length >= 2);
                var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(x => EF.Functions.ILike(x.Name, pattern));
            }
            if (barcode is not null) query = query.Where(x => x.Barcode == barcode);
            return Results.Ok(await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * 30).Take(30).ToListAsync(ct));
        });
        api.MapPut("/foods/{id:guid}", async (Guid id, FoodInput input, CurrentAccount a, AppDb db, CancellationToken ct) =>
        {
            Check.Text(input.Name); Check.That(input.Barcode is null || (input.Barcode.Length is >= 8 and <= 14 && input.Barcode.All(char.IsAsciiDigit)));
            Check.That(input.Calories100g is >= 0 and <= 1000 && input.Protein100g is >= 0 and <= 100 &&
                input.Carbs100g is >= 0 and <= 100 && input.Fat100g is >= 0 and <= 100);
            var existing = await db.Foods.SingleOrDefaultAsync(x => x.Id == id, ct);
            Check.That(existing is null || existing.AccountId == a.Id, "not_found", 404);
            // Immutable IDs preserve nutrition references and make retry semantics unambiguous.
            if (existing is not null)
            {
                Check.That(existing.Name == input.Name.Trim() && existing.Barcode == input.Barcode &&
                    existing.Calories100g == input.Calories100g && existing.Protein100g == input.Protein100g &&
                    existing.Carbs100g == input.Carbs100g && existing.Fat100g == input.Fat100g, "immutable_id_reused", 409);
                return Results.Ok(existing);
            }
            var food = new Food { Id = id, AccountId = a.Id, Name = input.Name.Trim(), Barcode = input.Barcode,
                Calories100g = input.Calories100g, Protein100g = input.Protein100g, Carbs100g = input.Carbs100g, Fat100g = input.Fat100g };
            db.Foods.Add(food); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/foods/{id}", food);
        });
        api.MapDelete("/foods/{id:guid}", async (Guid id, CurrentAccount a, AppDb db, CancellationToken ct) =>
            await db.Foods.Where(x => x.Id == id && x.AccountId == a.Id).ExecuteDeleteAsync(ct) == 1 ? Results.NoContent() : Results.NotFound());
    }

    public static async Task<List<MealItem>> Resolve(ItemInput[] inputs, Guid owner, AppDb db, CancellationToken ct)
    {
        Check.Items(inputs);
        var ids = inputs.Where(x => x.FoodId.HasValue).Select(x => x.FoodId!.Value).Distinct().ToArray();
        var foods = await db.Foods.AsNoTracking().Where(x => ids.Contains(x.Id) && (x.AccountId == null || x.AccountId == owner))
            .ToDictionaryAsync(x => x.Id, ct);
        Check.That(foods.Count == ids.Length, "food_not_found", 404);
        return inputs.Select(i => i.FoodId.HasValue ? FromFood(foods[i.FoodId.Value], i.Grams) : new MealItem
        { Name = i.Name.Trim(), Grams = i.Grams, Calories = i.Calories, Protein = i.Protein, Carbs = i.Carbs, Fat = i.Fat }).ToList();
    }

    private static MealItem FromFood(Food f, decimal grams) => new()
    {
        Name = f.Name, Grams = grams, Calories = Nutrition.Scale(f.Calories100g, grams),
        Protein = Nutrition.Scale(f.Protein100g, grams), Carbs = Nutrition.Scale(f.Carbs100g, grams), Fat = Nutrition.Scale(f.Fat100g, grams)
    };
}
