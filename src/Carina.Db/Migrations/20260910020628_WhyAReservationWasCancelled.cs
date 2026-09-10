using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhyAReservationWasCancelled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.AddColumn<string>(
            name: "cancelled_because",
            table: "reservation",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        // Everything already cancelled was cancelled by somebody pressing cancel, because until now
        // that was the only way a reservation left the running. Reading them as anything else would
        // give a rule leave to bring back rows a person meant to be rid of.
        migrationBuilder.Sql("UPDATE reservation SET cancelled_because = 'ByHand' WHERE state = 'Cancelled'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone', 'ProgrammeReturned')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation",
            sql: "(cancelled_because IS NULL OR cancelled_because IN ('ByHand', 'ProgrammeGone'))\nAND (state = 'Cancelled') = (cancelled_because IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation");

        migrationBuilder.DropColumn(
            name: "cancelled_because",
            table: "reservation");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone')");
    }
}
