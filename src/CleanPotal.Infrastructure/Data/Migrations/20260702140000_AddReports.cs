using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportType = table.Column<string>(type: "TEXT", nullable: false),
                    MonthTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ShortTitle = table.Column<string>(type: "TEXT", nullable: false),
                    DateRange = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: false),
                    MemoRich = table.Column<string>(type: "TEXT", nullable: false),
                    MainContent = table.Column<string>(type: "TEXT", nullable: false),
                    MainContentRich = table.Column<string>(type: "TEXT", nullable: false),
                    NightContent = table.Column<string>(type: "TEXT", nullable: false),
                    NightContentRich = table.Column<string>(type: "TEXT", nullable: false),
                    Attendees = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    MemoAttachments = table.Column<string>(type: "TEXT", nullable: false),
                    MainAttachments = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentRich = table.Column<string>(type: "TEXT", nullable: false),
                    FollowUp = table.Column<string>(type: "TEXT", nullable: false),
                    FollowUpRich = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Heading = table.Column<string>(type: "TEXT", nullable: false),
                    IsCollapsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Importance = table.Column<string>(type: "TEXT", nullable: false),
                    FollowUpAttachments = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportBlocks_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportBlocks_ReportId",
                table: "ReportBlocks",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReportBlocks");
            migrationBuilder.DropTable(name: "Reports");
        }
    }
}
