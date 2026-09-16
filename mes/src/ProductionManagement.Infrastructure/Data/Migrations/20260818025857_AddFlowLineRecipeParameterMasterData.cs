using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowLineRecipeParameterMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultLineId",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LineDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParameterDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ParameterType = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParameterDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductProcessFlows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessRouteId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProcessFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductProcessFlows_ProcessRoutes_ProcessRouteId",
                        column: x => x.ProcessRouteId,
                        principalTable: "ProcessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductProcessFlows_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductParameterAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParameterDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductParameterAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductParameterAssignments_ParameterDefinitions_ParameterDefinitionId",
                        column: x => x.ParameterDefinitionId,
                        principalTable: "ParameterDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductParameterAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductRecipeAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRecipeAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRecipeAssignments_ProcessDefinitions_ProcessDefinitionId",
                        column: x => x.ProcessDefinitionId,
                        principalTable: "ProcessDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductRecipeAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductRecipeAssignments_RecipeDefinitions_RecipeDefinitionId",
                        column: x => x.RecipeDefinitionId,
                        principalTable: "RecipeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_DefaultLineId",
                table: "Products",
                column: "DefaultLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LineDefinitions_Code",
                table: "LineDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParameterDefinitions_Code",
                table: "ParameterDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductParameterAssignments_ParameterDefinitionId",
                table: "ProductParameterAssignments",
                column: "ParameterDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductParameterAssignments_ProductId_ParameterDefinitionId",
                table: "ProductParameterAssignments",
                columns: new[] { "ProductId", "ParameterDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductProcessFlows_ProcessRouteId",
                table: "ProductProcessFlows",
                column: "ProcessRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductProcessFlows_ProductId_ProcessRouteId",
                table: "ProductProcessFlows",
                columns: new[] { "ProductId", "ProcessRouteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeAssignments_ProcessDefinitionId",
                table: "ProductRecipeAssignments",
                column: "ProcessDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeAssignments_ProductId_ProcessDefinitionId",
                table: "ProductRecipeAssignments",
                columns: new[] { "ProductId", "ProcessDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeAssignments_RecipeDefinitionId",
                table: "ProductRecipeAssignments",
                column: "RecipeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeDefinitions_Code",
                table: "RecipeDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_LineDefinitions_DefaultLineId",
                table: "Products",
                column: "DefaultLineId",
                principalTable: "LineDefinitions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_LineDefinitions_DefaultLineId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "LineDefinitions");

            migrationBuilder.DropTable(
                name: "ProductParameterAssignments");

            migrationBuilder.DropTable(
                name: "ProductProcessFlows");

            migrationBuilder.DropTable(
                name: "ProductRecipeAssignments");

            migrationBuilder.DropTable(
                name: "ParameterDefinitions");

            migrationBuilder.DropTable(
                name: "RecipeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Products_DefaultLineId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DefaultLineId",
                table: "Products");
        }
    }
}
