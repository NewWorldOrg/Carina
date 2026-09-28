using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AReservationStandsAsideForTheSameBroadcast : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation",
            sql: "(cancelled_because IS NULL OR cancelled_because IN ('ByHand', 'ProgrammeGone', 'SameBroadcast'))\nAND (state = 'Cancelled') = (cancelled_because IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation");

        migrationBuilder.Sql("UPDATE reservation SET cancelled_because = 'ByHand' WHERE cancelled_because = 'SameBroadcast'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_cancellation",
            table: "reservation",
            sql: "(cancelled_because IS NULL OR cancelled_because IN ('ByHand', 'ProgrammeGone'))\nAND (state = 'Cancelled') = (cancelled_because IS NOT NULL)");
    }
}
