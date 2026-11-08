using System.Text.Json;
using Kaloree.Api.Api;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Infrastructure;

public sealed record CatalogRow(Guid Id, string Source, FoodInput Food);

public static class CatalogImport
{
    // Operator-only CLI: no publicly exposed administrator endpoint.
    public static async Task Run(AppDb db, string path, CancellationToken ct)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 20 * 1024 * 1024) throw new InvalidOperationException("Catalog file must exist and be at most 20MiB");
        await using var stream = file.OpenRead();
        var rows = await JsonSerializer.DeserializeAsync<CatalogRow[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct)
            ?? throw new InvalidOperationException("Invalid catalog JSON");
        Check.That(rows.Length <= 100000 && rows.Select(x => x.Id).Distinct().Count() == rows.Length);
        foreach (var row in rows)
        {
            Check.That(row.Id != Guid.Empty); Check.Text(row.Source, 200); Check.Text(row.Food.Name);
            var f = row.Food;
            Check.That(f.Barcode is null || f.Barcode.Length <= 32);
            Check.That(f.Calories100g is >= 0 and <= 1000 && f.Protein100g is >= 0 and <= 100 &&
                f.Carbs100g is >= 0 and <= 100 && f.Fat100g is >= 0 and <= 100);
        }
        foreach (var batch in rows.Chunk(1000))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var row in batch)
            {
                var f = row.Food;
                var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Foods" ("Id", "AccountId", "Name", "Barcode", "Source", "Calories100g", "Protein100g", "Carbs100g", "Fat100g")
                    VALUES ({row.Id}, NULL, {f.Name.Trim()}, {f.Barcode}, {row.Source}, {f.Calories100g}, {f.Protein100g}, {f.Carbs100g}, {f.Fat100g})
                    ON CONFLICT ("Id") DO UPDATE SET "Name"=EXCLUDED."Name", "Barcode"=EXCLUDED."Barcode", "Source"=EXCLUDED."Source",
                        "Calories100g"=EXCLUDED."Calories100g", "Protein100g"=EXCLUDED."Protein100g", "Carbs100g"=EXCLUDED."Carbs100g", "Fat100g"=EXCLUDED."Fat100g"
                    WHERE "Foods"."AccountId" IS NULL
                    """, ct);
                if (affected != 1) throw new InvalidOperationException("A catalog ID conflicts with a private food ID");
            }
            await tx.CommitAsync(ct);
        }
    }
}
