using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class EncodeRunMarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "encoder_asked",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "encoder_ran",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "process_id",
            table: "encode_job",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "process_started_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "progress_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "progress_left",
            table: "encode_job",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "progress_portion",
            table: "encode_job",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "swerve",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_headway",
            table: "encode_job",
            sql: "(progress_at IS NULL OR status <> 'Queued')\nAND (progress_at IS NULL OR progress_at >= started_at)\nAND (progress_at IS NOT NULL OR (progress_portion IS NULL AND progress_left IS NULL))\nAND (progress_portion IS NULL OR progress_portion BETWEEN 0 AND 1)\nAND (progress_left IS NULL OR progress_left >= interval '0')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_programme",
            table: "encode_job",
            sql: "((process_id IS NULL) = (process_started_at IS NULL))\nAND (process_id IS NULL OR status = 'Running')\nAND (process_id IS NULL OR process_id >= 1)\nAND (process_started_at IS NULL OR process_started_at >= started_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job",
            sql: "((encoder_asked IS NULL) = (encoder_ran IS NULL))\nAND (encoder_asked IS NULL OR status <> 'Queued')\nAND (encoder_asked IS NULL OR encoder_asked IN ('Software', 'Vaapi'))\nAND (encoder_ran IS NULL OR encoder_ran IN ('Software', 'Vaapi'))\nAND (swerve IS NULL OR swerve IN ('TheCardIsOutOfReach', 'TheCardCannotDoThisCodec', 'TheProcessorCannotDoThisCodec'))\nAND (encoder_asked IS NULL OR ((swerve IS NULL) = (encoder_asked = encoder_ran)))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_headway",
            table: "encode_job");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_programme",
            table: "encode_job");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "encoder_asked",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "encoder_ran",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "process_id",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "process_started_at",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "progress_at",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "progress_left",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "progress_portion",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "swerve",
            table: "encode_job");
    }
}
