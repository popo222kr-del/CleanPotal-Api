using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationAndOperCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OperCode",
                table: "ProcessDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessRouteId",
                table: "Lots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RegistrationId",
                table: "Lots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepresentativeLotId",
                table: "Lots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Lots",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExportPrefix",
                table: "Customers",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Registrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExportNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Line = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProcessLabel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProcessRouteId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ShipDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RegisteredBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Registrations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Registrations_ProcessRoutes_ProcessRouteId",
                        column: x => x.ProcessRouteId,
                        principalTable: "ProcessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Registrations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessDefinitions_OperCode",
                table: "ProcessDefinitions",
                column: "OperCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ProcessRouteId",
                table: "Lots",
                column: "ProcessRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_RegistrationId",
                table: "Lots",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_RepresentativeLotId",
                table: "Lots",
                column: "RepresentativeLotId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_SerialNumber",
                table: "Lots",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ExportPrefix",
                table: "Customers",
                column: "ExportPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_CustomerId",
                table: "Registrations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_ExportNumber",
                table: "Registrations",
                column: "ExportNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_ProcessRouteId",
                table: "Registrations",
                column: "ProcessRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_ProductId",
                table: "Registrations",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lots_Lots_RepresentativeLotId",
                table: "Lots",
                column: "RepresentativeLotId",
                principalTable: "Lots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lots_ProcessRoutes_ProcessRouteId",
                table: "Lots",
                column: "ProcessRouteId",
                principalTable: "ProcessRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lots_Registrations_RegistrationId",
                table: "Lots",
                column: "RegistrationId",
                principalTable: "Registrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lots_Lots_RepresentativeLotId",
                table: "Lots");

            migrationBuilder.DropForeignKey(
                name: "FK_Lots_ProcessRoutes_ProcessRouteId",
                table: "Lots");

            migrationBuilder.DropForeignKey(
                name: "FK_Lots_Registrations_RegistrationId",
                table: "Lots");

            migrationBuilder.DropTable(
                name: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_ProcessDefinitions_OperCode",
                table: "ProcessDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Lots_ProcessRouteId",
                table: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_Lots_RegistrationId",
                table: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_Lots_RepresentativeLotId",
                table: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_Lots_SerialNumber",
                table: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ExportPrefix",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OperCode",
                table: "ProcessDefinitions");

            migrationBuilder.DropColumn(
                name: "ProcessRouteId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "RegistrationId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "RepresentativeLotId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "ExportPrefix",
                table: "Customers");
        }
    }
}
