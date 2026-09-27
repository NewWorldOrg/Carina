using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatWasLeftScrambledCanBeLifted : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<DateTime>(
            name: "descrambled_at",
            table: "reservation_outcome",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "descrambled_at",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_descrambled",
            table: "reservation_outcome",
            sql: "descrambled_at IS NULL\nOR (faults @> '[\"ScramblingUnresolved\"]'::jsonb\n    AND descrambled_at >= occurred_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_whole_but_scrambled",
            table: "reservation_outcome",
            sql: "kind <> 'RecordingFailure'\nOR recording_outcome <> 'Complete'\nOR faults @> '[\"ScramblingUnresolved\"]'::jsonb");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_descrambled",
            table: "recording",
            sql: "descrambled_at IS NULL\nOR (recording_outcome IS NOT NULL\n    AND recording_reasons_name_any(outcome_detail, ARRAY['ScramblingUnresolved']::text[])\n    AND descrambled_at >= stopped_at_actual)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_descrambled",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_whole_but_scrambled",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_descrambled",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "descrambled_at",
            table: "reservation_outcome");

        migrationBuilder.DropColumn(
            name: "descrambled_at",
            table: "recording");
    }
}
