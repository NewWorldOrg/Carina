using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class WhenAProgrammeWasLastHeard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.AddColumn<DateTime>(
            name: "last_heard_at",
            table: "programme",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE programme SET last_heard_at = now()");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.DropColumn(
            name: "last_heard_at",
            table: "programme");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure')");
    }
}
