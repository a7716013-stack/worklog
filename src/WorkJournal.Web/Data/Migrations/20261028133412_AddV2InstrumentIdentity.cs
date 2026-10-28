using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddV2InstrumentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "V2Instruments",
                columns: table => new
                {
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermanentKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstrumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2Instruments", x => x.InstrumentId);
                    table.CheckConstraint("CK_V2Instrument_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                });

            migrationBuilder.CreateTable(
                name: "V2CorporateActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2CorporateActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_V2CorporateActions_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2InstrumentSymbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2InstrumentSymbols", x => x.Id);
                    table.CheckConstraint("CK_V2Symbol_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_V2InstrumentSymbols_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2LegacyInstrumentMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StockId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2LegacyInstrumentMappings", x => x.Id);
                    table.CheckConstraint("CK_V2Legacy_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_V2LegacyInstrumentMappings_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_V2CorporateActions_InstrumentId_EffectiveAt",
                table: "V2CorporateActions",
                columns: new[] { "InstrumentId", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_V2Instruments_PermanentKey",
                table: "V2Instruments",
                column: "PermanentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2InstrumentSymbols_InstrumentId_ValidFrom",
                table: "V2InstrumentSymbols",
                columns: new[] { "InstrumentId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2InstrumentSymbols_Market_Symbol_ValidFrom",
                table: "V2InstrumentSymbols",
                columns: new[] { "Market", "Symbol", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2LegacyInstrumentMappings_InstrumentId",
                table: "V2LegacyInstrumentMappings",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_V2LegacyInstrumentMappings_Source_Market_StockId_ValidFrom",
                table: "V2LegacyInstrumentMappings",
                columns: new[] { "Source", "Market", "StockId", "ValidFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "V2CorporateActions");

            migrationBuilder.DropTable(
                name: "V2InstrumentSymbols");

            migrationBuilder.DropTable(
                name: "V2LegacyInstrumentMappings");

            migrationBuilder.DropTable(
                name: "V2Instruments");
        }
    }
}
