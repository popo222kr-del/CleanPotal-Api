using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBroken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrokenRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Line = table.Column<string>(type: "TEXT", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", nullable: false),
                    ProductType = table.Column<string>(type: "TEXT", nullable: false),
                    SN = table.Column<string>(type: "TEXT", nullable: false),
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    Causer = table.Column<string>(type: "TEXT", nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Career = table.Column<string>(type: "TEXT", nullable: false),
                    OccurStage = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsOfficial = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokenRecords", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokenRecords");
        }
    }
}
