using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCertificateTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CertificateTemplateData",
                table: "Products",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateTemplateFileName",
                table: "Products",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateTemplateData",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CertificateTemplateFileName",
                table: "Products");
        }
    }
}
