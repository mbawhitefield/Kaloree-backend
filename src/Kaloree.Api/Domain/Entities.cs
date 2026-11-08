namespace Kaloree.Api.Domain;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Subject { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly? BirthDate { get; set; }
    public string? EquationSex { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal ActivityFactor { get; set; } = 1.2m;
    public int? CalorieTarget { get; set; }
    public int ProteinPercent { get; set; } = 25;
    public int FatPercent { get; set; } = 30;
    public bool PhotoConsent { get; set; }
    public int Version { get; set; }
}

public abstract class OwnedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
}

public sealed class Food
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccountId { get; set; }
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public string Source { get; set; } = "custom";
    public decimal Calories100g { get; set; }
    public decimal Protein100g { get; set; }
    public decimal Carbs100g { get; set; }
    public decimal Fat100g { get; set; }
}

public sealed class Meal : OwnedEntity
{
    // Client-generated IDs make create retries safe. Nutrition is a historical snapshot.
    public DateOnly Date { get; set; }
    public string Slot { get; set; } = "lunch";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "manual";
    public List<MealItem> Items { get; set; } = [];
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MealItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MealId { get; set; }
    public string Name { get; set; } = "";
    public decimal Grams { get; set; }
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}

public sealed class WeightEntry : OwnedEntity
{
    public DateOnly Date { get; set; }
    public decimal Kg { get; set; }
}

public sealed class WaterEntry : OwnedEntity
{
    public DateOnly Date { get; set; }
    public int Millilitres { get; set; }
}

public sealed class Recipe : OwnedEntity
{
    public string Name { get; set; } = "";
    public int Servings { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public int Version { get; set; }
}

public sealed class Entitlement : OwnedEntity
{
    public string Product { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public long EventTimestamp { get; set; }
}

public sealed class BillingEvent
{
    public string Id { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AnalysisJob : OwnedEntity
{
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "queued";
    public byte[]? EncryptedPhoto { get; set; }
    public string MimeType { get; set; } = "";
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UsageBucket
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
