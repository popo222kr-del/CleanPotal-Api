using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterOperValueCountUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParameterDefinitions_Code",
                table: "ParameterDefinitions");

            migrationBuilder.AddColumn<string>(
                name: "Oper",
                table: "ParameterDefinitions",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "ParameterDefinitions",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValueCount",
                table: "ParameterDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ParameterDefinitions_Code_Oper",
                table: "ParameterDefinitions",
                columns: new[] { "Code", "Oper" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParameterDefinitions_Code_Oper",
                table: "ParameterDefinitions");

            migrationBuilder.DropColumn(
                name: "Oper",
                table: "ParameterDefinitions");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "ParameterDefinitions");

            migrationBuilder.DropColumn(
                name: "ValueCount",
                table: "ParameterDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_ParameterDefinitions_Code",
                table: "ParameterDefinitions",
                column: "Code",
                unique: true);
        }
    }
}
