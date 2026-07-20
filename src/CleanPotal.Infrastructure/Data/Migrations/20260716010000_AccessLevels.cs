using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// 권한 개편: 기능 플래그 8종 → 영역×등급 5종 (0 없음/1 조회/2 편집).
    /// 기존 사용자 매핑: 일정=CanManageSchedule?편집:조회, 근무표=CanManageShiftBoard?편집:조회,
    /// 인수인계·현장점검=편집(기존 전원 편집이었음), OFFICE=파일/BROKEN/기타 중 보유 시 편집, 아니면 없음.
    /// </summary>
    public partial class AccessLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "AccessSchedule", table: "Users", type: "INTEGER", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "AccessRoster", table: "Users", type: "INTEGER", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "AccessHandover", table: "Users", type: "INTEGER", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "AccessField", table: "Users", type: "INTEGER", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "AccessOffice", table: "Users", type: "INTEGER", nullable: false, defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE Users SET
                    AccessSchedule = CASE WHEN CanManageSchedule = 1 THEN 2 ELSE 1 END,
                    AccessRoster   = CASE WHEN CanManageShiftBoard = 1 THEN 2 ELSE 1 END,
                    AccessHandover = 2,
                    AccessField    = 2,
                    AccessOffice   = CASE WHEN CanManageFiles = 1 OR CanManageBroken = 1 OR CanAccessEtcMenu = 1 THEN 2 ELSE 0 END;
            ");

            migrationBuilder.DropColumn(name: "CanManageFiles", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageNotices", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageVendors", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageSchedule", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageBroken", table: "Users");
            migrationBuilder.DropColumn(name: "CanAccessEtcMenu", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageShiftBoard", table: "Users");
            migrationBuilder.DropColumn(name: "CanManageInventory", table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "CanManageFiles", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageNotices", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageVendors", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageSchedule", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageBroken", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanAccessEtcMenu", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageShiftBoard", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "CanManageInventory", table: "Users", type: "INTEGER", nullable: false, defaultValue: false);

            migrationBuilder.DropColumn(name: "AccessSchedule", table: "Users");
            migrationBuilder.DropColumn(name: "AccessRoster", table: "Users");
            migrationBuilder.DropColumn(name: "AccessHandover", table: "Users");
            migrationBuilder.DropColumn(name: "AccessField", table: "Users");
            migrationBuilder.DropColumn(name: "AccessOffice", table: "Users");
        }
    }
}
