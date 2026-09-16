using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessHistoryVoidAndDropRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rollbacks");

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "ProcessHistories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "ProcessHistories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "ProcessHistories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "ProcessHistories",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "ProcessHistories");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "ProcessHistories");

            migrationBuilder.CreateTable(
                name: "Rollbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromProcessDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToProcessDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FromStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ToStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rollbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rollbacks_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rollbacks_ProcessDefinitions_FromProcessDefinitionId",
                        column: x => x.FromProcessDefinitionId,
                        principalTable: "ProcessDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rollbacks_ProcessDefinitions_ToProcessDefinitionId",
                        column: x => x.ToProcessDefinitionId,
                        principalTable: "ProcessDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rollbacks_FromProcessDefinitionId",
                table: "Rollbacks",
                column: "FromProcessDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Rollbacks_LotId",
                table: "Rollbacks",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Rollbacks_ToProcessDefinitionId",
                table: "Rollbacks",
                column: "ToProcessDefinitionId");
        }
    }
}
