using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeDefinitionOperCodeAndReadTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OperCode",
                table: "RecipeDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReadTimeMinutes",
                table: "RecipeDefinitions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperCode",
                table: "RecipeDefinitions");

            migrationBuilder.DropColumn(
                name: "ReadTimeMinutes",
                table: "RecipeDefinitions");
        }
    }
}
