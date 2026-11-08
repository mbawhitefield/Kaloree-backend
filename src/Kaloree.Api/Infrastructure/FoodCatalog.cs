using System.Text.Json;
using Kaloree.Api.Api;
using Microsoft.Extensions.Caching.Memory;

namespace Kaloree.Api.Infrastructure;

public sealed record CatalogFood(string Barcode, string Name, decimal Calories100g, decimal Protein100g,
    decimal Carbs100g, decimal Fat100g, string Source, string Attribution, string License);

public sealed class FoodCatalog(HttpClient http, IConfiguration config) : IDisposable
{
    // Public reference data only; dedicated size-bounded cache, never used for authorization.
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2000 });
    public async Task<CatalogFood?> Barcode(string barcode, CancellationToken ct)
    {
        Check.That(barcode.Length is >= 8 and <= 14 && barcode.All(char.IsAsciiDigit));
        Check.That(config.GetValue<bool>("FoodCatalog:Enabled"), "catalog_disabled", 503);
        if (cache.TryGetValue(barcode, out CatalogFood? cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://world.openfoodfacts.org/api/v2/product/{barcode}.json?fields=code,product_name,nutriments");
        request.Headers.TryAddWithoutValidation("User-Agent", config["FoodCatalog:UserAgent"]);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new ApiException(503, "catalog_unavailable");
        await response.Content.LoadIntoBufferAsync(128 * 1024, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("product", out var product)) return null;
        var name = product.TryGetProperty("product_name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || !product.TryGetProperty("nutriments", out var nutrients)) return null;
        decimal? Read(string key) => nutrients.TryGetProperty(key, out var v) && v.TryGetDecimal(out var d) ? d : null;
        var kcal = Read("energy-kcal_100g"); var protein = Read("proteins_100g"); var carbs = Read("carbohydrates_100g"); var fat = Read("fat_100g");
        // Missing nutrition is unknown, never silently represented as zero.
        if (kcal is null or < 0 or > 1000 || protein is null or < 0 or > 100 || carbs is null or < 0 or > 100 || fat is null or < 0 or > 100) return null;
        var food = new CatalogFood(barcode, name, kcal.Value, protein.Value, carbs.Value, fat.Value,
            "Open Food Facts", "© Open Food Facts contributors", "ODbL / Database Contents License");
        cache.Set(barcode, food, new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(TimeSpan.FromHours(6)));
        return food;
    }
    public void Dispose() => cache.Dispose();
}
