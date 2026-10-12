using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketPulseDays",
                columns: table => new
                {
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPulseDays", x => new { x.TradeDate, x.Market });
                });

            migrationBuilder.CreateTable(
                name: "PortfolioDailyValues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Generation = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Cash = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false),
                    MarketValue = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false),
                    TotalAssets = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false),
                    InitialCash = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioDailyValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioDailyValues_PaperTradingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperTradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioDailyValues_AccountId_Generation_TradeDate",
                table: "PortfolioDailyValues",
                columns: new[] { "AccountId", "Generation", "TradeDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketPulseDays");

            migrationBuilder.DropTable(
                name: "PortfolioDailyValues");
        }
    }
}
