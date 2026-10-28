using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkJournal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddV2SnapshotsAndDecisionFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "V2BenchmarkDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2BenchmarkDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "V2DatasetSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DataCutoff = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DatasetVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SourceSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2DatasetSnapshots", x => x.Id);
                    table.CheckConstraint("CK_V2DatasetSnapshots_Status", "[Status] IN (0,1,2)");
                });

            migrationBuilder.CreateTable(
                name: "V2ParameterSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2ParameterSets", x => x.Id);
                    table.CheckConstraint("CK_V2ParameterSets_Status", "[Status] IN (0,1,2)");
                });

            migrationBuilder.CreateTable(
                name: "V2UniverseSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DataCutoff = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UniverseVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2UniverseSnapshots", x => x.Id);
                    table.CheckConstraint("CK_V2UniverseSnapshots_Status", "[Status] IN (0,1,2)");
                });

            migrationBuilder.CreateTable(
                name: "V2BenchmarkVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenchmarkDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2BenchmarkVersions", x => x.Id);
                    table.CheckConstraint("CK_V2Benchmark_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_V2BenchmarkVersions_V2BenchmarkDefinitions_BenchmarkDefinitionId",
                        column: x => x.BenchmarkDefinitionId,
                        principalTable: "V2BenchmarkDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2DataProvenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Dataset = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    QualityStatus = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RawReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2DataProvenance", x => x.Id);
                    table.CheckConstraint("CK_V2Provenance_Quality", "[QualityStatus] BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_V2DataProvenance_V2DatasetSnapshots_DatasetSnapshotId",
                        column: x => x.DatasetSnapshotId,
                        principalTable: "V2DatasetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2DataProvenance_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2UniverseMembers",
                columns: table => new
                {
                    UniverseSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EligibilityStatus = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2UniverseMembers", x => new { x.UniverseSnapshotId, x.InstrumentId });
                    table.CheckConstraint("CK_V2Universe_Eligibility", "[EligibilityStatus] BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_V2UniverseMembers_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2UniverseMembers_V2UniverseSnapshots_UniverseSnapshotId",
                        column: x => x.UniverseSnapshotId,
                        principalTable: "V2UniverseSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2BenchmarkMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenchmarkVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2BenchmarkMappings", x => x.Id);
                    table.CheckConstraint("CK_V2BenchmarkMapping_Period", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_V2BenchmarkMappings_V2BenchmarkVersions_BenchmarkVersionId",
                        column: x => x.BenchmarkVersionId,
                        principalTable: "V2BenchmarkVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2BenchmarkMappings_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2VersionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FeatureVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UniverseVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BenchmarkVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StrategyVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ExtensionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2VersionSets", x => x.Id);
                    table.CheckConstraint("CK_V2VersionSets_Status", "[Status] IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_V2VersionSets_V2BenchmarkVersions_BenchmarkVersionId",
                        column: x => x.BenchmarkVersionId,
                        principalTable: "V2BenchmarkVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "V2DecisionTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DecisionTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DataCutoff = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DatasetSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UniverseSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParameterSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionType = table.Column<int>(type: "int", nullable: false),
                    DecisionStatus = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_V2DecisionTraces", x => x.Id);
                    table.CheckConstraint("CK_V2DecisionTraces_Status", "[Status] IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_V2DecisionTraces_V2DatasetSnapshots_DatasetSnapshotId",
                        column: x => x.DatasetSnapshotId,
                        principalTable: "V2DatasetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2DecisionTraces_V2Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "V2Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2DecisionTraces_V2ParameterSets_ParameterSetId",
                        column: x => x.ParameterSetId,
                        principalTable: "V2ParameterSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2DecisionTraces_V2UniverseSnapshots_UniverseSnapshotId",
                        column: x => x.UniverseSnapshotId,
                        principalTable: "V2UniverseSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_V2DecisionTraces_V2VersionSets_VersionSetId",
                        column: x => x.VersionSetId,
                        principalTable: "V2VersionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_V2BenchmarkDefinitions_Code",
                table: "V2BenchmarkDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2BenchmarkMappings_BenchmarkVersionId",
                table: "V2BenchmarkMappings",
                column: "BenchmarkVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_V2BenchmarkMappings_InstrumentId_ValidFrom",
                table: "V2BenchmarkMappings",
                columns: new[] { "InstrumentId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2BenchmarkVersions_BenchmarkDefinitionId_Version",
                table: "V2BenchmarkVersions",
                columns: new[] { "BenchmarkDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2DataProvenance_DatasetSnapshotId_InstrumentId_AvailableAt",
                table: "V2DataProvenance",
                columns: new[] { "DatasetSnapshotId", "InstrumentId", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_V2DataProvenance_InstrumentId",
                table: "V2DataProvenance",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_V2DatasetSnapshots_Checksum",
                table: "V2DatasetSnapshots",
                column: "Checksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_Checksum",
                table: "V2DecisionTraces",
                column: "Checksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_DatasetSnapshotId",
                table: "V2DecisionTraces",
                column: "DatasetSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_InstrumentId_TradeDate_DecisionType",
                table: "V2DecisionTraces",
                columns: new[] { "InstrumentId", "TradeDate", "DecisionType" });

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_ParameterSetId",
                table: "V2DecisionTraces",
                column: "ParameterSetId");

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_UniverseSnapshotId",
                table: "V2DecisionTraces",
                column: "UniverseSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_V2DecisionTraces_VersionSetId",
                table: "V2DecisionTraces",
                column: "VersionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_V2ParameterSets_Checksum",
                table: "V2ParameterSets",
                column: "Checksum");

            migrationBuilder.CreateIndex(
                name: "IX_V2ParameterSets_Code_Version",
                table: "V2ParameterSets",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2UniverseMembers_InstrumentId",
                table: "V2UniverseMembers",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_V2UniverseSnapshots_Checksum",
                table: "V2UniverseSnapshots",
                column: "Checksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_V2VersionSets_BenchmarkVersionId",
                table: "V2VersionSets",
                column: "BenchmarkVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_V2VersionSets_Checksum",
                table: "V2VersionSets",
                column: "Checksum",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "V2BenchmarkMappings");

            migrationBuilder.DropTable(
                name: "V2DataProvenance");

            migrationBuilder.DropTable(
                name: "V2DecisionTraces");

            migrationBuilder.DropTable(
                name: "V2UniverseMembers");

            migrationBuilder.DropTable(
                name: "V2DatasetSnapshots");

            migrationBuilder.DropTable(
                name: "V2ParameterSets");

            migrationBuilder.DropTable(
                name: "V2VersionSets");

            migrationBuilder.DropTable(
                name: "V2UniverseSnapshots");

            migrationBuilder.DropTable(
                name: "V2BenchmarkVersions");

            migrationBuilder.DropTable(
                name: "V2BenchmarkDefinitions");
        }
    }
}
