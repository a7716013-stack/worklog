using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketRadarTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketRadarCaptureBatches",
                columns: table => new
                {
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarCaptureBatches", x => x.TradeDate);
                });

            migrationBuilder.CreateTable(
                name: "MarketRadarRecommendations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StockId = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsEtf = table.Column<bool>(type: "bit", nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScoreVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClosePrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true),
                    EarnedScore = table.Column<int>(type: "int", nullable: false),
                    AvailableScore = table.Column<int>(type: "int", nullable: false),
                    MomentumPoints = table.Column<int>(type: "int", nullable: true),
                    VolumePoints = table.Column<int>(type: "int", nullable: true),
                    TechnicalPoints = table.Column<int>(type: "int", nullable: true),
                    ChipPoints = table.Column<int>(type: "int", nullable: true),
                    FundamentalPoints = table.Column<int>(type: "int", nullable: true),
                    EventPoints = table.Column<int>(type: "int", nullable: true),
                    ForeignBuyStreak = table.Column<int>(type: "int", nullable: true),
                    ForeignSellStreak = table.Column<int>(type: "int", nullable: true),
                    TrustBuyStreak = table.Column<int>(type: "int", nullable: true),
                    TrustSellStreak = table.Column<int>(type: "int", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResearchJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketRadarDailySelections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    RecommendationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarDailySelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRadarDailySelections_MarketRadarCaptureBatches_TradeDate",
                        column: x => x.TradeDate,
                        principalTable: "MarketRadarCaptureBatches",
                        principalColumn: "TradeDate",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketRadarDailySelections_MarketRadarRecommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "MarketRadarRecommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketRadarPaperTradeLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecommendationId = table.Column<long>(type: "bigint", nullable: false),
                    PaperOrderId = table.Column<long>(type: "bigint", nullable: true),
                    OriginalPaperOrderId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarPaperTradeLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRadarPaperTradeLinks_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketRadarPaperTradeLinks_MarketRadarRecommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "MarketRadarRecommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketRadarPaperTradeLinks_PaperOrders_PaperOrderId",
                        column: x => x.PaperOrderId,
                        principalTable: "PaperOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MarketRadarPerformances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecommendationId = table.Column<long>(type: "bigint", nullable: false),
                    Return1D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Return3D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Return5D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Return10D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Return20D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Mfe5D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Mae5D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Mfe20D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    Mae20D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EntryPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    OpenReturn1D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    OpenReturn3D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    OpenReturn5D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    OpenReturn10D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    OpenReturn20D = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EvaluatedThroughTradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarPerformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRadarPerformances_MarketRadarRecommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "MarketRadarRecommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketRadarPersonalTrackings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecommendationId = table.Column<long>(type: "bigint", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PerformanceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRadarPersonalTrackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRadarPersonalTrackings_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketRadarPersonalTrackings_MarketRadarRecommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "MarketRadarRecommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarDailySelections_RecommendationId",
                table: "MarketRadarDailySelections",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarDailySelections_TradeDate_Rank",
                table: "MarketRadarDailySelections",
                columns: new[] { "TradeDate", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarDailySelections_TradeDate_RecommendationId",
                table: "MarketRadarDailySelections",
                columns: new[] { "TradeDate", "RecommendationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPaperTradeLinks_ApplicationUserId",
                table: "MarketRadarPaperTradeLinks",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPaperTradeLinks_PaperOrderId",
                table: "MarketRadarPaperTradeLinks",
                column: "PaperOrderId",
                unique: true,
                filter: "[PaperOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPaperTradeLinks_RecommendationId",
                table: "MarketRadarPaperTradeLinks",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPerformances_RecommendationId",
                table: "MarketRadarPerformances",
                column: "RecommendationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPersonalTrackings_ApplicationUserId_RecommendationId",
                table: "MarketRadarPersonalTrackings",
                columns: new[] { "ApplicationUserId", "RecommendationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarPersonalTrackings_RecommendationId",
                table: "MarketRadarPersonalTrackings",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarRecommendations_Industry",
                table: "MarketRadarRecommendations",
                column: "Industry");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarRecommendations_StockId",
                table: "MarketRadarRecommendations",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarRecommendations_TradeDate_Category_Score",
                table: "MarketRadarRecommendations",
                columns: new[] { "TradeDate", "Category", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketRadarRecommendations_TradeDate_StockId",
                table: "MarketRadarRecommendations",
                columns: new[] { "TradeDate", "StockId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketRadarDailySelections");

            migrationBuilder.DropTable(
                name: "MarketRadarPaperTradeLinks");

            migrationBuilder.DropTable(
                name: "MarketRadarPerformances");

            migrationBuilder.DropTable(
                name: "MarketRadarPersonalTrackings");

            migrationBuilder.DropTable(
                name: "MarketRadarCaptureBatches");

            migrationBuilder.DropTable(
                name: "MarketRadarRecommendations");
        }
    }
}
