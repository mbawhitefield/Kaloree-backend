namespace Kaloree.Api.Api;

public sealed class ApiException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class Check
{
    public static void That(bool valid, string code = "invalid_request", int status = 400)
    { if (!valid) throw new ApiException(status, code); }

    public static void Text(string? value, int max = 160) => That(!string.IsNullOrWhiteSpace(value) && value.Length <= max);
    public static void Date(DateOnly date) => That(date >= new DateOnly(2000, 1, 1) && date <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
    public static void Range(DateOnly from, DateOnly to, int max = 366)
    { That(to >= from && to.DayNumber - from.DayNumber <= max); }
    public static void Items(ItemInput[]? items)
    {
        That(items is { Length: > 0 and <= 50 });
        foreach (var i in items!)
        {
            That(i is not null);
            if (i is null) continue;
            Text(i.Name); That(i.Grams is > 0 and <= 10000);
            That(i.Calories is >= 0 and <= 20000 && i.Protein is >= 0 and <= 2000 &&
                 i.Carbs is >= 0 and <= 2000 && i.Fat is >= 0 and <= 2000);
        }
    }
}
