using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIcpms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcessType = table.Column<string>(type: "TEXT", nullable: false),
                    EqId = table.Column<string>(type: "TEXT", nullable: false),
                    BathGb = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    AnalysisDate = table.Column<string>(type: "TEXT", nullable: false),
                    Li = table.Column<double>(type: "REAL", nullable: false),
                    Na = table.Column<double>(type: "REAL", nullable: false),
                    Mg = table.Column<double>(type: "REAL", nullable: false),
                    Al = table.Column<double>(type: "REAL", nullable: false),
                    K = table.Column<double>(type: "REAL", nullable: false),
                    Ca = table.Column<double>(type: "REAL", nullable: false),
                    Ti = table.Column<double>(type: "REAL", nullable: false),
                    Cr = table.Column<double>(type: "REAL", nullable: false),
                    Mn = table.Column<double>(type: "REAL", nullable: false),
                    Fe = table.Column<double>(type: "REAL", nullable: false),
                    Co = table.Column<double>(type: "REAL", nullable: false),
                    Ni = table.Column<double>(type: "REAL", nullable: false),
                    Cu = table.Column<double>(type: "REAL", nullable: false),
                    Zn = table.Column<double>(type: "REAL", nullable: false),
                    Ge = table.Column<double>(type: "REAL", nullable: false),
                    As = table.Column<double>(type: "REAL", nullable: false),
                    Cd = table.Column<double>(type: "REAL", nullable: false),
                    In = table.Column<double>(type: "REAL", nullable: false),
                    Ba = table.Column<double>(type: "REAL", nullable: false),
                    Ta = table.Column<double>(type: "REAL", nullable: false),
                    W = table.Column<double>(type: "REAL", nullable: false),
                    Pb = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_EquipmentAnalyses", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAnalyses_ProcessType_EqId_BathGb_Category_AnalysisDate",
                table: "EquipmentAnalyses",
                columns: new[] { "ProcessType", "EqId", "BathGb", "Category", "AnalysisDate" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "EquipmentMasters",
                columns: table => new
                {
                    EqId = table.Column<string>(type: "TEXT", nullable: false),
                    Process = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_EquipmentMasters", x => x.EqId));

            migrationBuilder.CreateTable(
                name: "EquipmentCheckNotes",
                columns: table => new
                {
                    EqId = table.Column<string>(type: "TEXT", nullable: false),
                    CheckDate = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_EquipmentCheckNotes", x => new { x.EqId, x.CheckDate }));

            migrationBuilder.CreateTable(
                name: "EquipmentActionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActionType = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_EquipmentActionLogs", x => x.Id));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EquipmentAnalyses");
            migrationBuilder.DropTable(name: "EquipmentMasters");
            migrationBuilder.DropTable(name: "EquipmentCheckNotes");
            migrationBuilder.DropTable(name: "EquipmentActionLogs");
        }
    }
}
