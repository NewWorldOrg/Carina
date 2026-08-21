using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ArchivedProgrammeSearchColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "searchable",
            table: "archived_programme",
            type: "text",
            nullable: true,
            computedColumnSql: "lower(pg_catalog.normalize(name || ' ' || summary, 'NFKC'))",
            stored: true);

        migrationBuilder.AddColumn<int[]>(
            name: "genre_kinds",
            table: "archived_programme",
            type: "integer[]",
            nullable: true,
            computedColumnSql:
                "string_to_array(nullif(translate(jsonb_path_query_array(genres, '$[*].kind')::text, '[] ', ''), ''), ',')::integer[]",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "ix_archived_programme_start_at",
            table: "archived_programme",
            column: "start_at");

        migrationBuilder.Sql(
            "CREATE INDEX ix_archived_programme_searchable "
            + "ON archived_programme USING gin (searchable gin_trgm_ops);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_archived_programme_searchable;");

        migrationBuilder.DropIndex(
            name: "ix_archived_programme_start_at",
            table: "archived_programme");

        migrationBuilder.DropColumn(name: "genre_kinds", table: "archived_programme");
        migrationBuilder.DropColumn(name: "searchable", table: "archived_programme");
    }
}
