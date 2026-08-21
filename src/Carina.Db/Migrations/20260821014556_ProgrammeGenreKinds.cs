using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeGenreKinds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int[]>(
            name: "genre_kinds",
            table: "programme",
            type: "integer[]",
            nullable: true,
            computedColumnSql:
                "string_to_array(nullif(translate(jsonb_path_query_array(genres, '$[*].kind')::text, '[] ', ''), ''), ',')::integer[]",
            stored: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropColumn(name: "genre_kinds", table: "programme");
    }
}
