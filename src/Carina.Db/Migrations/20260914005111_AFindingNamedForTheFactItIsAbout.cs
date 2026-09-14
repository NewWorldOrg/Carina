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
        migrationBuilder.DropPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding");

        migrationBuilder.AddPrimaryKey(
            name: "pk_integrity_finding",
            table: "integrity_finding",
            column: "id");
    }
}
