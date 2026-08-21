using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeSearchNormalisation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            "ALTER TABLE programme ALTER COLUMN searchable "
            + "SET EXPRESSION AS (lower(pg_catalog.normalize(name || ' ' || summary, 'NFKC')));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            "ALTER TABLE programme ALTER COLUMN searchable "
            + "SET EXPRESSION AS (lower(name || ' ' || summary));");
    }
}
