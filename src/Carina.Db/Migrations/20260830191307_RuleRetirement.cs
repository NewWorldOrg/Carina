using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class RuleRetirement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_reservation_rule_rule_id",
            table: "reservation");

        migrationBuilder.AddForeignKey(
            name: "fk_reservation_rule_rule_id",
            table: "reservation",
            column: "rule_id",
            principalTable: "rule",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_reservation_rule_rule_id",
            table: "reservation");

        migrationBuilder.AddForeignKey(
            name: "fk_reservation_rule_rule_id",
            table: "reservation",
            column: "rule_id",
            principalTable: "rule",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }
}
