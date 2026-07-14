using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProdReqImagesAndReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestImages",
                table: "ProdReqs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActionImages",
                table: "ProdReqs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProdReqReads",
                columns: table => new
                {
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    LastReadTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdReqReads", x => x.Username);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProdReqReads");
            migrationBuilder.DropColumn(name: "ActionImages", table: "ProdReqs");
            migrationBuilder.DropColumn(name: "RequestImages", table: "ProdReqs");
        }
    }
}
