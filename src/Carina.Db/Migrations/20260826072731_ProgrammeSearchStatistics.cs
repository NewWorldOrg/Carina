using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeSearchStatistics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN searchable SET STATISTICS 1000;");
        migrationBuilder.Sql("ALTER TABLE archived_programme ALTER COLUMN searchable SET STATISTICS 1000;");
        migrationBuilder.Sql("ANALYZE programme, archived_programme;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN searchable SET STATISTICS -1;");
        migrationBuilder.Sql("ALTER TABLE archived_programme ALTER COLUMN searchable SET STATISTICS -1;");
        migrationBuilder.Sql("ANALYZE programme, archived_programme;");
    }
}
