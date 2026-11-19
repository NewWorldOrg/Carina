using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class TheDayBoundaryCarriedRulesKeep : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss",
            sql: "subject IN ('DuplicateAvoidance', 'EnclosedCharacters', 'DayBoundary')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss",
            sql: "subject IN ('DuplicateAvoidance', 'EnclosedCharacters')");
    }
}
