using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperTradingAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    InitialCash = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Cash = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    RealizedProfitLoss = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Generation = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperTradingAccounts", x => x.Id);
                    table.CheckConstraint("CK_PaperAccount_Cash", "[Cash] >= 0 AND [InitialCash] > 0");
                    table.CheckConstraint("CK_PaperAccount_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "PaperOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockId = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsEtf = table.Column<bool>(type: "bit", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    OrderType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LimitPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FilledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FilledPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SubmittedTradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EligibleFromTradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastEvaluatedTradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FilledTradeDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperOrders", x => x.Id);
                    table.CheckConstraint("CK_PaperOrder_Enums", "[Side] IN (0,1) AND [OrderType] IN (0,1) AND [Status] IN (0,1,2,3)");
                    table.CheckConstraint("CK_PaperOrder_Limit", "([OrderType] = 0 AND [LimitPrice] IS NULL) OR ([OrderType] = 1 AND [LimitPrice] > 0)");
                    table.CheckConstraint("CK_PaperOrder_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_PaperOrders_PaperTradingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperTradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperPositions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CostBasis = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    RealizedProfitLoss = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperPositions", x => x.Id);
                    table.CheckConstraint("CK_PaperPosition_Quantity", "[Quantity] >= 0 AND [CostBasis] >= 0 AND [AverageCost] >= 0");
                    table.ForeignKey(
                        name: "FK_PaperPositions_PaperTradingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperTradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperTrades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionSequence = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    TransactionTax = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    NetCashAmount = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    RealizedProfitLoss = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    QuoteTradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperTrades", x => x.Id);
                    table.CheckConstraint("CK_PaperTrade_Amounts", "[Quantity] > 0 AND [Price] > 0 AND [Commission] >= 0 AND [TransactionTax] >= 0");
                    table.ForeignKey(
                        name: "FK_PaperTrades_PaperOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaperOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperTrades_PaperTradingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperTradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_AccountId_ClientRequestId",
                table: "PaperOrders",
                columns: new[] { "AccountId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_AccountId_Status_CreatedAt",
                table: "PaperOrders",
                columns: new[] { "AccountId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperPositions_AccountId_StockId",
                table: "PaperPositions",
                columns: new[] { "AccountId", "StockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrades_AccountId_ExecutedAt",
                table: "PaperTrades",
                columns: new[] { "AccountId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrades_OrderId_ExecutionSequence",
                table: "PaperTrades",
                columns: new[] { "OrderId", "ExecutionSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperPositions");

            migrationBuilder.DropTable(
                name: "PaperTrades");

            migrationBuilder.DropTable(
                name: "PaperOrders");

            migrationBuilder.DropTable(
                name: "PaperTradingAccounts");
        }
    }
}
