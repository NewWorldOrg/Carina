using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class MigrationRecord : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "migration_run",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                pass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_run", x => x.id);
                table.CheckConstraint("ck_migration_run_pass", "pass IN ('Rehearsal', 'ForReal')");
                table.CheckConstraint("ck_migration_run_source", "length(source) > 0");
                table.CheckConstraint("ck_migration_run_span", "finished_at >= started_at");
            });

        migrationBuilder.CreateTable(
            name: "migration_detail",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                population = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                refusal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                claimed = table.Column<long>(type: "bigint", nullable: true),
                observed = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_detail", x => x.id);
                table.CheckConstraint("ck_migration_detail_population", "population IN ('Recordings', 'RecordingFiles', 'Rules', 'Reservations', 'ChannelDefinitions')");
                table.CheckConstraint("ck_migration_detail_refusal", "refusal IN ('ReallyEmpty', 'FileMissing', 'Orphan', 'Unidentifiable', 'Inexpressible', 'NoSuchFeature', 'OutOfScope')");
                table.CheckConstraint("ck_migration_detail_sizes", "(claimed IS NULL OR claimed >= 0) AND (observed IS NULL OR observed >= 0)");
                table.CheckConstraint("ck_migration_detail_subject", "length(subject) > 0");
                table.ForeignKey(
                    name: "fk_migration_detail_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "migration_omission",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ground = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_omission", x => new { x.run_id, x.subject });
                table.CheckConstraint("ck_migration_omission_ground", "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')");
                table.CheckConstraint("ck_migration_omission_subject", "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory')");
                table.ForeignKey(
                    name: "fk_migration_omission_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "migration_tally",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                population = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                offered = table.Column<int>(type: "integer", nullable: false),
                carried = table.Column<int>(type: "integer", nullable: false),
                not_carried = table.Column<int>(type: "integer", nullable: false),
                unclassified = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_tally", x => new { x.run_id, x.population });
                table.CheckConstraint("ck_migration_tally_counts", "offered >= 0\nAND carried >= 0\nAND not_carried >= 0\nAND unclassified >= 0\nAND carried + not_carried + unclassified = offered");
                table.CheckConstraint("ck_migration_tally_population", "population IN ('Recordings', 'RecordingFiles', 'Rules', 'Reservations', 'ChannelDefinitions')");
                table.ForeignKey(
                    name: "fk_migration_tally_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_migration_detail_run",
            table: "migration_detail",
            columns: new[] { "run_id", "refusal" });

        migrationBuilder.CreateIndex(
            name: "ux_migration_detail_subject",
            table: "migration_detail",
            columns: new[] { "run_id", "population", "subject" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_migration_run_finished",
            table: "migration_run",
            column: "finished_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "migration_detail");

        migrationBuilder.DropTable(
            name: "migration_omission");

        migrationBuilder.DropTable(
            name: "migration_tally");

        migrationBuilder.DropTable(
            name: "migration_run");
    }
}
