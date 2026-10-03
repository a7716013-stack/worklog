using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarBidirectionalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkLogs",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "CalendarSyncLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkLogId = table.Column<int>(type: "int", nullable: true),
                    GoogleEventId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Baseline = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Retired = table.Column<bool>(type: "bit", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Problem = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarSyncLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarSyncLinks_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarSyncLinks_WorkLogs_WorkLogId",
                        column: x => x.WorkLogId,
                        principalTable: "WorkLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSyncLinks_ApplicationUserId_GoogleEventId",
                table: "CalendarSyncLinks",
                columns: new[] { "ApplicationUserId", "GoogleEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSyncLinks_WorkLogId",
                table: "CalendarSyncLinks",
                column: "WorkLogId",
                unique: true,
                filter: "[WorkLogId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarSyncLinks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkLogs");
        }
    }
}
