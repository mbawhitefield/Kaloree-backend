using Kaloree.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kaloree.Api.Infrastructure;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<WeightEntry> Weights => Set<WeightEntry>();
    public DbSet<WaterEntry> Water => Set<WaterEntry>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<UsageBucket> UsageBuckets => Set<UsageBucket>();
    public DbSet<PlannedMeal> PlannedMeals => Set<PlannedMeal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("pg_trgm");
        b.Entity<Account>().HasIndex(x => x.Subject).IsUnique();
        b.Entity<Account>().Property(x => x.Subject).HasMaxLength(200);
        b.Entity<Account>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Food>().HasIndex(x => new { x.AccountId, x.Name });
        b.Entity<Food>().HasIndex(x => x.Barcode);
        b.Entity<Food>().HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Food>().Property(x => x.Name).HasMaxLength(160);
        b.Entity<Food>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        Owned<Meal>(b); Owned<WeightEntry>(b); Owned<WaterEntry>(b);
        Owned<Recipe>(b); Owned<Entitlement>(b); Owned<AnalysisJob>(b);
        Owned<PlannedMeal>(b);
        b.Entity<PlannedMeal>().HasIndex(x => new { x.AccountId, x.Date });
        b.Entity<PlannedMeal>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PlannedMeal>().Property(x => x.ItemsJson).HasColumnType("jsonb");
        b.Entity<MealItem>().ToTable(t => t.HasCheckConstraint("CK_MealItem_Positive", "\"Grams\" > 0 AND \"Calories\" >= 0 AND \"Protein\" >= 0 AND \"Carbs\" >= 0 AND \"Fat\" >= 0"));
        b.Entity<WeightEntry>().ToTable("Weights", t => t.HasCheckConstraint("CK_Weight_Range", "\"Kg\" BETWEEN 30 AND 400"));
        b.Entity<WaterEntry>().ToTable("Water", t => t.HasCheckConstraint("CK_Water_Range", "\"Millilitres\" BETWEEN 1 AND 5000"));
        b.Entity<Food>().ToTable("Foods", t => t.HasCheckConstraint("CK_Food_Nutrients", "\"Calories100g\" BETWEEN 0 AND 1000 AND \"Protein100g\" BETWEEN 0 AND 100 AND \"Carbs100g\" BETWEEN 0 AND 100 AND \"Fat100g\" BETWEEN 0 AND 100"));
        b.Entity<Meal>().HasIndex(x => new { x.AccountId, x.Date, x.Id });
        b.Entity<Meal>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Meal>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.MealId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WeightEntry>().HasIndex(x => new { x.AccountId, x.Date }).IsUnique();
        b.Entity<WaterEntry>().HasIndex(x => new { x.AccountId, x.Date });
        b.Entity<Recipe>().Property(x => x.ItemsJson).HasColumnType("jsonb");
        b.Entity<Recipe>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Entitlement>().HasIndex(x => new { x.AccountId, x.Product }).IsUnique();
        b.Entity<AnalysisJob>().HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
        b.Entity<AnalysisJob>().HasIndex(x => new { x.Status, x.AvailableAt });
        b.Entity<AnalysisJob>().HasIndex(x => x.CreatedAt);
        b.Entity<AnalysisJob>().Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.Entity<AnalysisJob>().Property(x => x.ResultJson).HasColumnType("jsonb");
        b.Entity<BillingEvent>().Property(x => x.Id).HasMaxLength(200);
        b.Entity<UsageBucket>().HasKey(x => x.Key);
        b.Entity<UsageBucket>().Property(x => x.Key).HasMaxLength(200);
        b.Entity<UsageBucket>().HasIndex(x => x.ExpiresAt);
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
            { p.SetPrecision(12); p.SetScale(3); }
    }

    private static void Owned<T>(ModelBuilder b) where T : OwnedEntity =>
        b.Entity<T>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
}
