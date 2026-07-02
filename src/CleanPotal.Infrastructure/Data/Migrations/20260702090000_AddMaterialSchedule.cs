using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialDayNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NoteAm = table.Column<string>(type: "TEXT", nullable: false),
                    NotePm = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialDayNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRosterMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRosterMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PersonName = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    Vehicles = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialScheduleEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialDayNotes_TargetDate",
                table: "MaterialDayNotes",
                column: "TargetDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialScheduleEntries_TargetDate_PersonName_Period",
                table: "MaterialScheduleEntries",
                columns: new[] { "TargetDate", "PersonName", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialDayNotes");

            migrationBuilder.DropTable(
                name: "MaterialRosterMembers");

            migrationBuilder.DropTable(
                name: "MaterialScheduleEntries");
        }
    }
}
