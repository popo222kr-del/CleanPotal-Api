using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations;

/// <summary>견적서 작성자(당사) 이메일 컬럼 추가.</summary>
public partial class AddQuotationAetsEmail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AetsEmail",
            table: "Quotations",
            type: "TEXT",
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AetsEmail",
            table: "Quotations");
    }
}
