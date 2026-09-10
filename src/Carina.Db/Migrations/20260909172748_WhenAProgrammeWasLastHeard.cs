using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhenAProgrammeWasLastHeard : Migration
{
    /// <inheritdoc />
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

        // Everything already on the shelf counts as heard now, so the first reading that hears a
        // service whole after this is what separates the broadcasts still announced from the ones
        // that are not. Leaving them empty would make every one of them unanswerable for ever.
        migrationBuilder.Sql("UPDATE programme SET last_heard_at = now()");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone')");
    }

    /// <inheritdoc />
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
