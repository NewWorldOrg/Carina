using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class TheSkipThresholdReachesNinetyNineHundredths : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings",
            sql: "skip_threshold BETWEEN 0.50 AND 0.99");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings");

        migrationBuilder.Sql("UPDATE segment_settings SET skip_threshold = 0.95 WHERE skip_threshold > 0.95");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings",
            sql: "skip_threshold BETWEEN 0.50 AND 0.95");
    }
}
