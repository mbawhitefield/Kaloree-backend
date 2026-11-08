using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaloree.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlansAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "PlannedMeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Slot = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedMeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedMeals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Weight_Range",
                table: "Weights",
                sql: "\"Kg\" BETWEEN 30 AND 400");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Water_Range",
                table: "Water",
                sql: "\"Millilitres\" BETWEEN 1 AND 5000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MealItem_Positive",
                table: "MealItem",
                sql: "\"Grams\" > 0 AND \"Calories\" >= 0 AND \"Protein\" >= 0 AND \"Carbs\" >= 0 AND \"Fat\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Name",
                table: "Foods",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Food_Nutrients",
                table: "Foods",
                sql: "\"Calories100g\" BETWEEN 0 AND 1000 AND \"Protein100g\" BETWEEN 0 AND 100 AND \"Carbs100g\" BETWEEN 0 AND 100 AND \"Fat100g\" BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedMeals_AccountId_Date",
                table: "PlannedMeals",
                columns: new[] { "AccountId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannedMeals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Weight_Range",
                table: "Weights");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Water_Range",
                table: "Water");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MealItem_Positive",
                table: "MealItem");

            migrationBuilder.DropIndex(
                name: "IX_Foods_Name",
                table: "Foods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Food_Nutrients",
                table: "Foods");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
