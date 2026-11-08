using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaloree.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EquationSex = table.Column<string>(type: "text", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    ActivityFactor = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    CalorieTarget = table.Column<int>(type: "integer", nullable: true),
                    ProteinPercent = table.Column<int>(type: "integer", nullable: false),
                    FatPercent = table.Column<int>(type: "integer", nullable: false),
                    PhotoConsent = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageBuckets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageBuckets", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EncryptedPhoto = table.Column<byte[]>(type: "bytea", nullable: true),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisJobs_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Product = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EventTimestamp = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entitlements_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Barcode = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Calories100g = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Protein100g = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Carbs100g = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Fat100g = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Foods_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Meals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Slot = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Servings = table.Column<int>(type: "integer", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Water",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Millilitres = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Water", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Water_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Weights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Weights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Weights_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Grams = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Calories = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Protein = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Carbs = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Fat = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealItem_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Subject",
                table: "Accounts",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_AccountId_IdempotencyKey",
                table: "AnalysisJobs",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_CreatedAt",
                table: "AnalysisJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_Status_AvailableAt",
                table: "AnalysisJobs",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_AccountId_Product",
                table: "Entitlements",
                columns: new[] { "AccountId", "Product" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foods_AccountId_Name",
                table: "Foods",
                columns: new[] { "AccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Barcode",
                table: "Foods",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_MealItem_MealId",
                table: "MealItem",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_AccountId_Date_Id",
                table: "Meals",
                columns: new[] { "AccountId", "Date", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_AccountId",
                table: "Recipes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageBuckets_ExpiresAt",
                table: "UsageBuckets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Water_AccountId_Date",
                table: "Water",
                columns: new[] { "AccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Weights_AccountId_Date",
                table: "Weights",
                columns: new[] { "AccountId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisJobs");

            migrationBuilder.DropTable(
                name: "BillingEvents");

            migrationBuilder.DropTable(
                name: "Entitlements");

            migrationBuilder.DropTable(
                name: "Foods");

            migrationBuilder.DropTable(
                name: "MealItem");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "UsageBuckets");

            migrationBuilder.DropTable(
                name: "Water");

            migrationBuilder.DropTable(
                name: "Weights");

            migrationBuilder.DropTable(
                name: "Meals");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
