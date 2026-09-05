using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class LibrarySearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int[]>(
            name: "genre_kinds",
            table: "recording",
            type: "integer[]",
            nullable: true,
            computedColumnSql:
                "string_to_array(nullif(translate(jsonb_path_query_array(snapshot_genres, '$[*].kind')::text, '[] ', ''), ''), ',')::integer[]",
            stored: true);

        migrationBuilder.AddColumn<string>(
            name: "searchable",
            table: "recording",
            type: "text",
            nullable: true,
            computedColumnSql:
                "lower(pg_catalog.normalize(snapshot_name || ' ' || snapshot_summary || ' ' || snapshot_extended, 'NFKC'))",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "ix_recording_library",
            table: "recording",
            columns: ["started_at_actual", "id"],
            descending: []);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_recording_library",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "genre_kinds",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "searchable",
            table: "recording");
    }
}
