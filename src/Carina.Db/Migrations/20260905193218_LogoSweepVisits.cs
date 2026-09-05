using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class LogoSweepVisits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_signal_sample_purpose",
            table: "quality_signal_sample");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_session_measurement_purpose",
            table: "quality_session_measurement");

        migrationBuilder.CreateTable(
            name: "logo_visit",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                last_attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_logo_visit", x => new { x.network_id, x.transport_stream_id });
                table.CheckConstraint("ck_logo_visit_collected", "(outcome <> 'Collected') OR (last_collected_at IS NOT NULL)");
                table.CheckConstraint("ck_logo_visit_outcome", "outcome IN ('Collected', 'NothingArrived', 'NoLock', 'Interrupted')");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_signal_sample_purpose",
            table: "quality_signal_sample",
            sql: "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow', 'Logo')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_session_measurement_purpose",
            table: "quality_session_measurement",
            sql: "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow', 'Logo')\nAND purpose <> 'Recording'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "logo_visit");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_signal_sample_purpose",
            table: "quality_signal_sample");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_session_measurement_purpose",
            table: "quality_session_measurement");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_signal_sample_purpose",
            table: "quality_signal_sample",
            sql: "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_session_measurement_purpose",
            table: "quality_session_measurement",
            sql: "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow')\nAND purpose <> 'Recording'");
    }
}
