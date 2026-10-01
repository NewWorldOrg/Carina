using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ARecordingCountsEverySessionThatWroteIt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<long>(
            name: "carried_cc_dropped_packets",
            table: "recording",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "carried_cc_total_packets",
            table: "recording",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "carried_drop_positions",
            table: "recording",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.AddColumn<long>(
            name: "carried_eovf_count",
            table: "recording",
            type: "bigint",
            nullable: false,
            defaultValueSql: "0");

        migrationBuilder.AddColumn<long>(
            name: "carried_pcr_anchor",
            table: "recording",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "carried_pcr_reanchors",
            table: "recording",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.AddColumn<long>(
            name: "carried_scrambled_packets",
            table: "recording",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "counted_session_opened_at",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_what_was_carried",
            table: "recording",
            sql: "(carried_cc_dropped_packets IS NULL) = (carried_cc_total_packets IS NULL)\nAND (carried_cc_total_packets IS NULL\n    OR (cc_measured\n        AND carried_cc_dropped_packets BETWEEN 0 AND cc_dropped_packets\n        AND carried_cc_total_packets BETWEEN carried_cc_dropped_packets AND cc_total_packets))\nAND (carried_scrambled_packets IS NULL\n    OR (scrambled_packets IS NOT NULL\n        AND carried_scrambled_packets BETWEEN 0 AND scrambled_packets))\nAND carried_eovf_count BETWEEN 0 AND eovf_count\nAND (carried_pcr_anchor IS NOT NULL\n    OR (recording_json_count(carried_drop_positions) = 0\n        AND recording_json_count(carried_pcr_reanchors) = 0))\nAND (carried_pcr_anchor IS NULL\n    OR (carried_cc_total_packets IS NOT NULL\n        AND carried_pcr_anchor BETWEEN 0 AND 8589934591))\nAND recording_positions_hold(carried_drop_positions, carried_cc_dropped_packets, carried_scrambled_packets)\nAND recording_reanchors_hold(carried_pcr_reanchors, 8589934592)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_what_was_carried",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_cc_dropped_packets",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_cc_total_packets",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_drop_positions",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_eovf_count",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_pcr_anchor",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_pcr_reanchors",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "carried_scrambled_packets",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "counted_session_opened_at",
            table: "recording");
    }
}
