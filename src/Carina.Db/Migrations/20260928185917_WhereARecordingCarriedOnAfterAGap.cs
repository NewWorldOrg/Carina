using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhereARecordingCarriedOnAfterAGap : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "gaps",
            table: "recording",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.AddColumn<long>(
            name: "missed_ms",
            table: "recording",
            type: "bigint",
            nullable: false,
            defaultValueSql: "0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_gaps",
            table: "recording",
            sql: "jsonb_typeof(gaps) = 'array' AND missed_ms >= 0 AND (missed_ms = 0) = (jsonb_array_length(gaps) = 0)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_gaps",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "gaps",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "missed_ms",
            table: "recording");
    }
}
