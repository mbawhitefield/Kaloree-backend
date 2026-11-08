namespace Kaloree.Api.Api;

public sealed record ProfileInput(DateOnly BirthDate, string EquationSex, decimal HeightCm, decimal WeightKg,
    decimal ActivityFactor, bool PhotoConsent, int Version);
public sealed record GoalInput(int Calories, int ProteinPercent, int FatPercent, int Version);
public sealed record FoodInput(string Name, string? Barcode, decimal Calories100g, decimal Protein100g, decimal Carbs100g, decimal Fat100g);
public sealed record ItemInput(Guid? FoodId, string Name, decimal Grams, decimal Calories, decimal Protein, decimal Carbs, decimal Fat);
public sealed record MealInput(DateOnly Date, string Slot, string Name, string Source, ItemInput[] Items, int Version);
public sealed record WeightInput(DateOnly Date, decimal Kg);
public sealed record WaterInput(DateOnly Date, int Millilitres);
public sealed record RecipeInput(string Name, int Servings, ItemInput[] Items, int Version);
public sealed record PhotoInput(string MimeType, string Base64, bool Consent);
public sealed record PhotoFood(string Name, decimal Grams, decimal Calories, decimal Protein, decimal Carbs, decimal Fat, string Confidence);
public sealed record PhotoEstimate(PhotoFood[] Foods, string[] Questions, string Warning);
