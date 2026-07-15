using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanPotal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleEquipmentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Process",
                table: "ScheduleEquipments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ScheduleEquipments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsIdle",
                table: "ScheduleEquipments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Process", table: "ScheduleEquipments");
            migrationBuilder.DropColumn(name: "Note", table: "ScheduleEquipments");
            migrationBuilder.DropColumn(name: "IsIdle", table: "ScheduleEquipments");
        }
    }
}
