using Microsoft.OpenApi;

namespace Kaloree.Api.Api;

public static class ApiCategories
{
    private static readonly (string Name, string Description)[] Categories =
    [
        ("Authentication", "Register, log in, refresh access and sign out. No email verification is required."),
        ("Account security", "Change your password and manage authenticator verification."),
        ("Account and data", "Provision your nutrition account, export data and delete your account."),
        ("Profile and goals", "Manage your adult profile, calorie targets and nutrition calculations."),
        ("Foods and barcodes", "Search foods, manage custom foods and look up packaged foods."),
        ("Meal diary", "Log, update and remove meals."),
        ("Water tracking", "Record and review daily water intake."),
        ("Weight tracking", "Record and review weight measurements."),
        ("Nutrition summaries", "Review daily calorie and macronutrient totals."),
        ("Recipes", "Create and manage personal recipes."),
        ("Meal planning", "Manage premium meal plans."),
        ("Photo analysis", "Submit, review and delete premium meal-photo estimates."),
        ("Subscriptions", "Check server-verified premium entitlement status."),
        ("Billing webhooks", "Receive signed billing-provider events; not a mobile client endpoint."),
        ("Health", "Check application liveness and database readiness.")
    ];

    public static void Apply(OpenApiDocument document)
    {
        document.Tags = Categories.Select(c => new OpenApiTag { Name = c.Name, Description = c.Description }).ToHashSet();
        foreach (var path in document.Paths)
        {
            var name = Category(path.Key);
            foreach (var operation in path.Value.Operations?.Values.AsEnumerable() ?? [])
                operation.Tags = new HashSet<OpenApiTagReference> { new(name, document) };
        }
    }

    private static string Category(string path) => path switch
    {
        var p when p.StartsWith("/health/", StringComparison.Ordinal) => "Health",
        "/webhooks/revenuecat" => "Billing webhooks",
        var p when p.StartsWith("/api/v1/auth/manage/", StringComparison.Ordinal) => "Account security",
        var p when p.StartsWith("/api/v1/auth/", StringComparison.Ordinal) => "Authentication",
        "/api/v1/account" or "/api/v1/export" => "Account and data",
        "/api/v1/profile" or "/api/v1/goals" or "/api/v1/calculations" => "Profile and goals",
        "/api/v1/subscription" => "Subscriptions",
        var p when p.StartsWith("/api/v1/catalog/", StringComparison.Ordinal) || p.StartsWith("/api/v1/foods", StringComparison.Ordinal) => "Foods and barcodes",
        var p when p.StartsWith("/api/v1/meals", StringComparison.Ordinal) => "Meal diary",
        var p when p.StartsWith("/api/v1/water", StringComparison.Ordinal) => "Water tracking",
        var p when p.StartsWith("/api/v1/weights", StringComparison.Ordinal) => "Weight tracking",
        "/api/v1/summary" => "Nutrition summaries",
        var p when p.StartsWith("/api/v1/recipes", StringComparison.Ordinal) => "Recipes",
        var p when p.StartsWith("/api/v1/plans", StringComparison.Ordinal) => "Meal planning",
        var p when p.StartsWith("/api/v1/analyses", StringComparison.Ordinal) => "Photo analysis",
        _ => throw new InvalidOperationException($"No API category configured for {path}")
    };
}
