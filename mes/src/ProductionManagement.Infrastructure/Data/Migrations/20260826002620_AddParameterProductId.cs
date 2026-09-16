using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterProductId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParameterDefinitions_Code_Oper",
                table: "ParameterDefinitions");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "ParameterDefinitions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParameterDefinitions_ProductId_Code_Oper",
                table: "ParameterDefinitions",
                columns: new[] { "ProductId", "Code", "Oper" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParameterDefinitions_ProductId_Code_Oper",
                table: "ParameterDefinitions");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "ParameterDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_ParameterDefinitions_Code_Oper",
                table: "ParameterDefinitions",
                columns: new[] { "Code", "Oper" },
                unique: true);
        }
    }
}
