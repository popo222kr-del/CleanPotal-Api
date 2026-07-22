using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations;

/// <summary>견적 기준정보 확장 — 주소/전화/팩스/서명자/회사명 (PDF 양식용).</summary>
public partial class ExtendQuotationConfig : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Address", table: "QuotationConfigs", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Tel", table: "QuotationConfigs", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Fax", table: "QuotationConfigs", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Signer", table: "QuotationConfigs", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "CompanyName", table: "QuotationConfigs", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Address", table: "QuotationConfigs");
        migrationBuilder.DropColumn(name: "Tel", table: "QuotationConfigs");
        migrationBuilder.DropColumn(name: "Fax", table: "QuotationConfigs");
        migrationBuilder.DropColumn(name: "Signer", table: "QuotationConfigs");
        migrationBuilder.DropColumn(name: "CompanyName", table: "QuotationConfigs");
    }
}
