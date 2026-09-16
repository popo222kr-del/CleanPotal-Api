using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRecipeMultiSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductRecipeAssignments_ProductId_ProcessDefinitionId",
                table: "ProductRecipeAssignments");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProductRecipeAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMain",
                table: "ProductRecipeAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxValue",
                table: "ProductRecipeAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinValue",
                table: "ProductRecipeAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeAssignments_ProductId_ProcessDefinitionId_RecipeDefinitionId",
                table: "ProductRecipeAssignments",
                columns: new[] { "ProductId", "ProcessDefinitionId", "RecipeDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductRecipeAssignments_ProductId_ProcessDefinitionId_RecipeDefinitionId",
                table: "ProductRecipeAssignments");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProductRecipeAssignments");

            migrationBuilder.DropColumn(
                name: "IsMain",
                table: "ProductRecipeAssignments");

            migrationBuilder.DropColumn(
                name: "MaxValue",
                table: "ProductRecipeAssignments");

            migrationBuilder.DropColumn(
                name: "MinValue",
                table: "ProductRecipeAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeAssignments_ProductId_ProcessDefinitionId",
                table: "ProductRecipeAssignments",
                columns: new[] { "ProductId", "ProcessDefinitionId" },
                unique: true);
        }
    }
}
