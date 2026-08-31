using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ReservationOutcomeLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_reservation_outcome_reservation",
            table: "reservation_outcome");

        migrationBuilder.CreateIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome",
            columns: ["reservation_id", "kind"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome");

        migrationBuilder.CreateIndex(
            name: "ix_reservation_outcome_reservation",
            table: "reservation_outcome",
            column: "reservation_id");
    }
}
