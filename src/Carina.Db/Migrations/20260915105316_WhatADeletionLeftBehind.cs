using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatADeletionLeftBehind : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int>(
            name: "files_left_behind",
            table: "recording",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "left_behind_at",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_left_behind",
            table: "recording",
            sql: "(files_left_behind IS NULL OR left_behind_at IS NOT NULL)\nAND (files_left_behind IS NULL OR files_left_behind > 0)\nAND (left_behind_at IS NULL OR recording_outcome IS NOT NULL)\nAND (left_behind_at IS NULL OR left_behind_at >= started_at_actual)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_left_behind",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "files_left_behind",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "left_behind_at",
            table: "recording");
    }
}
