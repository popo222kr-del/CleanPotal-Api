using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations;

/// <summary>생산팀 요청사항 등록 옵션(구분/세부 위치/요청 분류) 테이블 + 기존 하드코딩 값 시드.</summary>
public partial class AddProdReqOptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProdReqOptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Parent = table.Column<string>(type: "TEXT", nullable: false),
                OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_ProdReqOptions", x => x.Id));

        // 기존 하드코딩 옵션 시드 (동작 그대로 유지)
        migrationBuilder.Sql(@"
INSERT INTO ProdReqOptions (Kind, Name, Parent, OrderIndex) VALUES
('category','METAL','',0),
('subloc','입고실','METAL',1),('subloc','출고실','METAL',2),('subloc','세정실','METAL',3),('subloc','반입구','METAL',4),
('category','N-METAL','',5),
('subloc','입고실','N-METAL',6),('subloc','출고실','N-METAL',7),('subloc','세정실','N-METAL',8),('subloc','반입구','N-METAL',9),
('category','레이저실','',10),
('subloc','LASER','레이저실',11),('subloc','CO2','레이저실',12),('subloc','각인기','레이저실',13),('subloc','기타','레이저실',14),
('category','기타','',15),
('subloc','기타','기타',16),
('reqtype','소모품','',17),('reqtype','수리','',18),('reqtype','내용','',19),('reqtype','기타','',20);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProdReqOptions");
    }
}
