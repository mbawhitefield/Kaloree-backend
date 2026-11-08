namespace Kaloree.Api.Domain;

public sealed class PlannedMeal : OwnedEntity
{
    public DateOnly Date { get; set; }
    public string Slot { get; set; } = "lunch";
    public string Name { get; set; } = "";
    public string ItemsJson { get; set; } = "[]";
    public int Version { get; set; }
}
