using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class EncodeLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "encode_profile",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                deinterlace = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                rate_factor = table.Column<int>(type: "integer", nullable: false),
                quantiser = table.Column<int>(type: "integer", nullable: false),
                defined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_profile", x => x.id);
                table.CheckConstraint("ck_encode_profile_codec", "codec IN ('H264', 'H265')");
                table.CheckConstraint("ck_encode_profile_deinterlace", "deinterlace IN ('Leave', 'EveryFrame', 'EveryField')");
                table.CheckConstraint("ck_encode_profile_label", "btrim(label) = label AND length(label) > 0");
                table.CheckConstraint("ck_encode_profile_rate_control", "rate_factor BETWEEN 0 AND 51\nAND quantiser BETWEEN 0 AND 51");
                table.CheckConstraint("ck_encode_profile_resolution", "resolution IN ('AsSource', 'FullHd', 'Hd')");
            });

        migrationBuilder.CreateTable(
            name: "encode_destination",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                default_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                defined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_destination", x => x.id);
                table.CheckConstraint("ck_encode_destination_label", "btrim(label) = label AND length(label) > 0");
                table.CheckConstraint("ck_encode_destination_output_root", "btrim(output_root) = output_root\nAND length(output_root) > 0\nAND output_root <> '.'\nAND strpos(output_root, '/') = 0\nAND strpos(output_root, chr(92)) = 0\nAND strpos(output_root, '..') = 0");
                table.ForeignKey(
                    name: "fk_encode_destination_encode_profile_default_profile_id",
                    column: x => x.default_profile_id,
                    principalTable: "encode_profile",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "encode_job",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                artefact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                failure_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                failure_noticed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_job", x => x.id);
                table.CheckConstraint("ck_encode_job_artefact", "(status <> 'Completed' OR artefact_name IS NOT NULL)\nAND (artefact_name IS NULL\n    OR (btrim(artefact_name) = artefact_name\nAND length(artefact_name) > 0\nAND artefact_name <> '.'\nAND strpos(artefact_name, '/') = 0\nAND strpos(artefact_name, chr(92)) = 0\nAND strpos(artefact_name, '..') = 0\n        AND strpos(artefact_name, replace(recording_id::text, '-', '')) > 0\n        AND strpos(artefact_name, replace(profile_id::text, '-', '')) > 0))");
                table.CheckConstraint("ck_encode_job_attempt", "attempt >= 1");
                table.CheckConstraint("ck_encode_job_failure", "((status = 'Failed') = (failure IS NOT NULL))\nAND ((failure IS NULL) = (failure_note IS NULL))\nAND ((failure IS NULL) = (failure_noticed_at IS NULL))\nAND (failure IS NULL OR failure IN ('FfmpegExitedNonZero', 'NotEnoughRoom', 'SourceMissing', 'CapabilityUnavailable', 'TimedOut', 'DestinationCollision'))");
                table.CheckConstraint("ck_encode_job_output_root", "btrim(output_root) = output_root\nAND length(output_root) > 0\nAND output_root <> '.'\nAND strpos(output_root, '/') = 0\nAND strpos(output_root, chr(92)) = 0\nAND strpos(output_root, '..') = 0");
                table.CheckConstraint("ck_encode_job_status", "status IN ('Queued', 'Running', 'Completed', 'Failed', 'Cancelled')");
                table.CheckConstraint("ck_encode_job_timeline", "((status = 'Queued') = (started_at IS NULL AND ended_at IS NULL))\nAND ((status = 'Running') = (started_at IS NOT NULL AND ended_at IS NULL))\nAND ((status IN ('Completed', 'Failed', 'Cancelled')) = (ended_at IS NOT NULL))\nAND (started_at IS NULL OR started_at >= queued_at)\nAND (ended_at IS NULL OR ended_at >= queued_at)\nAND (ended_at IS NULL OR started_at IS NULL OR ended_at >= started_at)");
                table.ForeignKey(
                    name: "fk_encode_job_encode_destination_destination_id",
                    column: x => x.destination_id,
                    principalTable: "encode_destination",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_encode_job_encode_profile_profile_id",
                    column: x => x.profile_id,
                    principalTable: "encode_profile",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "encode_scratch_file",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                written_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                fate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_scratch_file", x => x.id);
                table.CheckConstraint("ck_encode_scratch_file_kind", "kind IN ('WorkFile', 'Chapters')");
                table.CheckConstraint("ck_encode_scratch_file_name", "btrim(file_name) = file_name\nAND length(file_name) > 0\nAND file_name <> '.'\nAND strpos(file_name, '/') = 0\nAND strpos(file_name, chr(92)) = 0\nAND strpos(file_name, '..') = 0");
                table.CheckConstraint("ck_encode_scratch_file_output_root", "btrim(output_root) = output_root\nAND length(output_root) > 0\nAND output_root <> '.'\nAND strpos(output_root, '/') = 0\nAND strpos(output_root, chr(92)) = 0\nAND strpos(output_root, '..') = 0");
                table.CheckConstraint("ck_encode_scratch_file_removal", "((removed_at IS NULL) = (fate IS NULL))\nAND (fate IS NULL OR fate IN ('Removed', 'AlreadyGone', 'BecameTheArtefact', 'CouldNotBeRemoved'))\nAND (removed_at IS NULL OR removed_at >= written_at)");
                table.ForeignKey(
                    name: "fk_encode_scratch_file_encode_job_job_id",
                    column: x => x.job_id,
                    principalTable: "encode_job",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_encode_destination_default_profile_id",
            table: "encode_destination",
            column: "default_profile_id");

        migrationBuilder.CreateIndex(
            name: "ix_encode_job_destination_id",
            table: "encode_job",
            column: "destination_id");

        migrationBuilder.CreateIndex(
            name: "ix_encode_job_profile_id",
            table: "encode_job",
            column: "profile_id");

        migrationBuilder.CreateIndex(
            name: "ix_encode_job_queued",
            table: "encode_job",
            column: "queued_at",
            filter: "status = 'Queued'");

        migrationBuilder.CreateIndex(
            name: "ix_encode_job_recording",
            table: "encode_job",
            columns: new[] { "recording_id", "queued_at" });

        migrationBuilder.CreateIndex(
            name: "ux_encode_job_artefact",
            table: "encode_job",
            columns: new[] { "output_root", "artefact_name" },
            unique: true,
            filter: "artefact_name IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_encode_job_running",
            table: "encode_job",
            column: "status",
            unique: true,
            filter: "status = 'Running'");

        migrationBuilder.CreateIndex(
            name: "ix_encode_scratch_file_owed",
            table: "encode_scratch_file",
            column: "job_id",
            filter: "removed_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ux_encode_scratch_file_name",
            table: "encode_scratch_file",
            columns: new[] { "output_root", "file_name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "encode_scratch_file");

        migrationBuilder.DropTable(
            name: "encode_job");

        migrationBuilder.DropTable(
            name: "encode_destination");

        migrationBuilder.DropTable(
            name: "encode_profile");
    }
}
