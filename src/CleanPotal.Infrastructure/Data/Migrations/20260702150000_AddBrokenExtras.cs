using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokenExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "PositionFrozen", table: "BrokenRecords", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "IncidentReports", table: "BrokenRecords", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "CountermeasureReports", table: "BrokenRecords", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "TrainingDocs", table: "BrokenRecords", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "TrainingImages", table: "BrokenRecords", type: "TEXT", nullable: false, defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BrokenTrainings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrainingType = table.Column<string>(type: "TEXT", nullable: false),
                    TrainingDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Documents = table.Column<string>(type: "TEXT", nullable: false),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokenTrainings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrokenGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokenGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrokenMetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Memo = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokenMetas", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BrokenTrainings");
            migrationBuilder.DropTable(name: "BrokenGoals");
            migrationBuilder.DropTable(name: "BrokenMetas");
            migrationBuilder.DropColumn(name: "PositionFrozen", table: "BrokenRecords");
            migrationBuilder.DropColumn(name: "IncidentReports", table: "BrokenRecords");
            migrationBuilder.DropColumn(name: "CountermeasureReports", table: "BrokenRecords");
            migrationBuilder.DropColumn(name: "TrainingDocs", table: "BrokenRecords");
            migrationBuilder.DropColumn(name: "TrainingImages", table: "BrokenRecords");
        }
    }
}
