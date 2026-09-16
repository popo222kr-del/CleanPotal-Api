using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessHistoryRecipeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecipeDefinitionId",
                table: "ProcessHistories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessHistories_RecipeDefinitionId",
                table: "ProcessHistories",
                column: "RecipeDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessHistories_RecipeDefinitions_RecipeDefinitionId",
                table: "ProcessHistories",
                column: "RecipeDefinitionId",
                principalTable: "RecipeDefinitions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessHistories_RecipeDefinitions_RecipeDefinitionId",
                table: "ProcessHistories");

            migrationBuilder.DropIndex(
                name: "IX_ProcessHistories_RecipeDefinitionId",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "RecipeDefinitionId",
                table: "ProcessHistories");
        }
    }
}
