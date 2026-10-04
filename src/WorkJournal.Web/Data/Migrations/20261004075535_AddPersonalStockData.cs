using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalStockData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaperAccount_Singleton",
                table: "PaperTradingAccounts");

            migrationBuilder.CreateSequence<int>(
                name: "PaperAccountIds",
                startValue: 2L);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "PaperTradingAccounts",
                type: "int",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR [PaperAccountIds]",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationUserId",
                table: "PaperTradingAccounts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockWatchlistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsEtf = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockWatchlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockWatchlistItems_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperTradingAccounts_ApplicationUserId",
                table: "PaperTradingAccounts",
                column: "ApplicationUserId",
                unique: true,
                filter: "[ApplicationUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockWatchlistItems_ApplicationUserId_Symbol",
                table: "StockWatchlistItems",
                columns: new[] { "ApplicationUserId", "Symbol" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperTradingAccounts_AspNetUsers_ApplicationUserId",
                table: "PaperTradingAccounts",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PaperTradingAccounts WHERE Id <> 1 OR ApplicationUserId IS NOT NULL) OR EXISTS (SELECT 1 FROM StockWatchlistItems) THROW 51002, 'Personal stock data exists. Back up and migrate it before reverting this schema.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_PaperTradingAccounts_AspNetUsers_ApplicationUserId",
                table: "PaperTradingAccounts");

            migrationBuilder.DropTable(
                name: "StockWatchlistItems");

            migrationBuilder.DropIndex(
                name: "IX_PaperTradingAccounts_ApplicationUserId",
                table: "PaperTradingAccounts");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "PaperTradingAccounts");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "PaperTradingAccounts",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValueSql: "NEXT VALUE FOR [PaperAccountIds]");

            migrationBuilder.DropSequence(
                name: "PaperAccountIds");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaperAccount_Singleton",
                table: "PaperTradingAccounts",
                sql: "[Id] = 1");
        }
    }
}
