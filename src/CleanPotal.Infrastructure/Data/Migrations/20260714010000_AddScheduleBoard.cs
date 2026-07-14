using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardDate = table.Column<string>(type: "TEXT", nullable: false),
                    EquipmentIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    S2Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HFMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DIMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    S2Temperature = table.Column<int>(type: "INTEGER", nullable: true),
                    RecipeText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    S2Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HFMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DIMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    S2Temperature = table.Column<int>(type: "INTEGER", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRecipes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_BoardDate",
                table: "ScheduleBlocks",
                column: "BoardDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduleBlocks");
            migrationBuilder.DropTable(name: "ScheduleRecipes");
        }
    }
}
