using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessTransitionDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TranId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceOperCode = table.Column<int>(type: "INTEGER", nullable: false),
                    TranCode = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetOperCode = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessTransitionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReasonCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReasonCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTransitionDefinitions_SourceOperCode_IsActive",
                table: "ProcessTransitionDefinitions",
                columns: new[] { "SourceOperCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTransitionDefinitions_TranId",
                table: "ProcessTransitionDefinitions",
                column: "TranId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReasonCodes_Category_Code",
                table: "ReasonCodes",
                columns: new[] { "Category", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessTransitionDefinitions");

            migrationBuilder.DropTable(
                name: "ReasonCodes");
        }
    }
}
