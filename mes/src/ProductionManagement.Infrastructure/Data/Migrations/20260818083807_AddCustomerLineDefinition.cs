using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLineDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LineDefinitionId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LineDefinitionId",
                table: "Customers",
                column: "LineDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_LineDefinitions_LineDefinitionId",
                table: "Customers",
                column: "LineDefinitionId",
                principalTable: "LineDefinitions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_LineDefinitions_LineDefinitionId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_LineDefinitionId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LineDefinitionId",
                table: "Customers");
        }
    }
}
