using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyScanningAndSignalSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StrategySignalId",
                table: "PaperOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScanProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    UniverseCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UniverseVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IncludeTWSE = table.Column<bool>(type: "bit", nullable: false),
                    IncludeTPEx = table.Column<bool>(type: "bit", nullable: false),
                    MinHistoryDays = table.Column<int>(type: "int", nullable: false),
                    MinAvgVolume20D = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    MinAvgTradingValue20D = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    RadarMinScore = table.Column<int>(type: "int", nullable: false),
                    BenchmarkMinScore = table.Column<int>(type: "int", nullable: false),
                    RecommendationMinScore = table.Column<int>(type: "int", nullable: false),
                    RecommendationGradeScore = table.Column<int>(type: "int", nullable: false),
                    StrongRecommendationMinScore = table.Column<int>(type: "int", nullable: false),
                    TrendVolumeRatio = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    BreakoutVolumeRatio = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    ForeignBuyDays = table.Column<int>(type: "int", nullable: false),
                    TrustBuyDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyDailyStocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RadarScore = table.Column<int>(type: "int", nullable: true),
                    SwingLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyDailyStocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategySourceCaches",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySourceCaches", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "StrategyEpisodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StrategyCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StrategyVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedTradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastMatchedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndedTradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntrySignalLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HasObservationGap = table.Column<bool>(type: "bit", nullable: false),
                    PriorStateUnknown = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedDays = table.Column<int>(type: "int", nullable: false),
                    GapDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyEpisodes_ScanProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "ScanProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StrategyScanRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    UniverseCount = table.Column<int>(type: "int", nullable: false),
                    QualifiedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    PendingCount = table.Column<int>(type: "int", nullable: false),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    RecommendationCount = table.Column<int>(type: "int", nullable: false),
                    ApiCalls = table.Column<int>(type: "int", nullable: false),
                    CacheHits = table.Column<int>(type: "int", nullable: false),
                    ElapsedMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyScanRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyScanRuns_ScanProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "ScanProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StrategySignals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EpisodeId = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    StrategyCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StrategyVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SignalLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategySignals_StrategyEpisodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "StrategyEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StrategyEvaluations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StrategyCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StrategyVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EligibilityStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyEvaluations_StrategyScanRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "StrategyScanRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StrategySignalPerformances",
                columns: table => new
                {
                    StrategySignalId = table.Column<long>(type: "bigint", nullable: false),
                    NextOpenDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextOpen = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    IsReferenceOnly = table.Column<bool>(type: "bit", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySignalPerformances", x => x.StrategySignalId);
                    table.ForeignKey(
                        name: "FK_StrategySignalPerformances_StrategySignals_StrategySignalId",
                        column: x => x.StrategySignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StrategySignalSnapshots",
                columns: table => new
                {
                    StrategySignalId = table.Column<long>(type: "bigint", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    MarketHeat = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ScoreVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataAvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySignalSnapshots", x => x.StrategySignalId);
                    table.ForeignKey(
                        name: "FK_StrategySignalSnapshots_StrategySignals_StrategySignalId",
                        column: x => x.StrategySignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_StrategySignalId",
                table: "PaperOrders",
                column: "StrategySignalId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanProfiles_Code_Version",
                table: "ScanProfiles",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDailyStocks_ProfileId_TradeDate_Symbol",
                table: "StrategyDailyStocks",
                columns: new[] { "ProfileId", "TradeDate", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyEpisodes_ProfileId_Symbol_StrategyCode_StrategyVersion",
                table: "StrategyEpisodes",
                columns: new[] { "ProfileId", "Symbol", "StrategyCode", "StrategyVersion" },
                unique: true,
                filter: "[EndedTradeDate] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyEvaluations_ProfileId_TradeDate_Symbol_StrategyCode_StrategyVersion",
                table: "StrategyEvaluations",
                columns: new[] { "ProfileId", "TradeDate", "Symbol", "StrategyCode", "StrategyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyEvaluations_RunId",
                table: "StrategyEvaluations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyScanRuns_ProfileId",
                table: "StrategyScanRuns",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyScanRuns_TradeDate_ProfileId",
                table: "StrategyScanRuns",
                columns: new[] { "TradeDate", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_EpisodeId",
                table: "StrategySignals",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_ProfileId_TradeDate_Symbol_StrategyCode_StrategyVersion",
                table: "StrategySignals",
                columns: new[] { "ProfileId", "TradeDate", "Symbol", "StrategyCode", "StrategyVersion" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperOrders_StrategySignals_StrategySignalId",
                table: "PaperOrders",
                column: "StrategySignalId",
                principalTable: "StrategySignals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperOrders_StrategySignals_StrategySignalId",
                table: "PaperOrders");

            migrationBuilder.DropTable(
                name: "StrategyDailyStocks");

            migrationBuilder.DropTable(
                name: "StrategyEvaluations");

            migrationBuilder.DropTable(
                name: "StrategySignalPerformances");

            migrationBuilder.DropTable(
                name: "StrategySignalSnapshots");

            migrationBuilder.DropTable(
                name: "StrategySourceCaches");

            migrationBuilder.DropTable(
                name: "StrategyScanRuns");

            migrationBuilder.DropTable(
                name: "StrategySignals");

            migrationBuilder.DropTable(
                name: "StrategyEpisodes");

            migrationBuilder.DropTable(
                name: "ScanProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PaperOrders_StrategySignalId",
                table: "PaperOrders");

            migrationBuilder.DropColumn(
                name: "StrategySignalId",
                table: "PaperOrders");
        }
    }
}
