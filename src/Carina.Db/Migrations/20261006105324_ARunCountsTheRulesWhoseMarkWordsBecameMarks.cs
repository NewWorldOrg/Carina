using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ARunCountsTheRulesWhoseMarkWordsBecameMarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss",
            sql: "subject IN ('DuplicateAvoidance', 'EnclosedCharacters', 'DayBoundary', 'MarkWords')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_loss_subject",
            table: "migration_loss",
            sql: "subject IN ('DuplicateAvoidance', 'EnclosedCharacters', 'DayBoundary')");
    }
}
