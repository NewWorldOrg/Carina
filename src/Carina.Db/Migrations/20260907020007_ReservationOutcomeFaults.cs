using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ReservationOutcomeFaults : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "faults",
            table: "reservation_outcome",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome",
            sql: "faults <@ '[\"TuneFailed\", \"RefusedByDiskPrecheck\", \"DiskExhausted\", \"DriverLost\", \"DrainGraceExpired\", \"StoppedByHand\", \"TunerContended\", \"ScramblingUnresolved\", \"ShortOfTheWindow\", \"NothingLanded\", \"SizeUnobserved\", \"StoppedUnasked\", \"LighterThanTheStream\", \"HeavierThanTheStream\"]'::jsonb\nAND (kind <> 'TuneFailure' OR faults @> '[\"TuneFailed\"]'::jsonb)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome");

        migrationBuilder.DropColumn(
            name: "faults",
            table: "reservation_outcome");
    }
}
