using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProdReq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProdReqs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    RequestDetail = table.Column<string>(type: "TEXT", nullable: false),
                    Requester = table.Column<string>(type: "TEXT", nullable: false),
                    ActionDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ActionDetail = table.Column<string>(type: "TEXT", nullable: false),
                    Assignee = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdReqs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProdReqs");
        }
    }
}
