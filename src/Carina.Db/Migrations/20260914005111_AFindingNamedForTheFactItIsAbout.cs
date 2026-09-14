using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AFindingNamedForTheFactItIsAbout : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding");

        migrationBuilder.AddPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding",
            columns: ["check_id", "id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM integrity_finding AS older
            USING integrity_finding AS newer,
                  integrity_check AS older_check,
                  integrity_check AS newer_check
            WHERE older.id = newer.id
              AND older_check.id = older.check_id
              AND newer_check.id = newer.check_id
              AND (older_check.started_at, older.check_id) < (newer_check.started_at, newer.check_id);
            """);

        migrationBuilder.DropPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding");

        migrationBuilder.AddPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding",
            column: "id");
    }
}
