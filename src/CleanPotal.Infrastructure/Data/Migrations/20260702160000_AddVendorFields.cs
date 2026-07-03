using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "IsFavorite", table: "Vendors", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "BasePath", table: "Vendors", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Addresses", table: "Vendors", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Managers", table: "Vendors", type: "TEXT", nullable: false, defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsFavorite", table: "Vendors");
            migrationBuilder.DropColumn(name: "BasePath", table: "Vendors");
            migrationBuilder.DropColumn(name: "Addresses", table: "Vendors");
            migrationBuilder.DropColumn(name: "Managers", table: "Vendors");
        }
    }
}
