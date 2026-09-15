using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatNoRecordingOwnsIsThrownAway : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_written_at",
            table: "integrity_finding",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "thrown_away_at",
            table: "integrity_finding",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_last_written",
            table: "integrity_finding",
            sql: "last_written_at IS NULL OR fault IN ('NoLedgerRow')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_thrown_away",
            table: "integrity_finding",
            sql: "thrown_away_at IS NULL\nOR (fault IN ('NoLedgerRow') AND last_written_at IS NOT NULL AND thrown_away_at >= noticed_at)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_last_written",
            table: "integrity_finding");

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_thrown_away",
            table: "integrity_finding");

        migrationBuilder.DropColumn(
            name: "last_written_at",
            table: "integrity_finding");

        migrationBuilder.DropColumn(
            name: "thrown_away_at",
            table: "integrity_finding");
    }
}
