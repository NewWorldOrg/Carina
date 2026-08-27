using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ReservationReception : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "reception_unavailable",
            table: "reservation",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "reception_unavailable_since",
            table: "reservation",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_reception",
            table: "reservation",
            sql: "reception_unavailable = (reception_unavailable_since IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_reception",
            table: "reservation");

        migrationBuilder.DropColumn(
            name: "reception_unavailable",
            table: "reservation");

        migrationBuilder.DropColumn(
            name: "reception_unavailable_since",
            table: "reservation");
    }
}
