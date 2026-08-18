using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeSearchIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "searchable",
            table: "programme",
            type: "text",
            nullable: true,
            computedColumnSql: "lower(name || ' ' || summary)",
            stored: true);

        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql(
            "CREATE INDEX ix_programme_searchable ON programme USING gin (searchable gin_trgm_ops);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_programme_searchable;");
        migrationBuilder.DropColumn(name: "searchable", table: "programme");
    }
}
