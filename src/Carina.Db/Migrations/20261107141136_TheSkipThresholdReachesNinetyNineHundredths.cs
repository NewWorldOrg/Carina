using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets the threshold a skip button is offered from reach 0.99: 1.00 would offer no button at all, and the 0.95 before
/// this had no reason behind it. Going back brings a threshold above 0.95 down to 0.95, since the check before this one
/// cannot hold it.
/// </summary>
public partial class TheSkipThresholdReachesNinetyNineHundredths : Migration
{
    /// <inheritdoc />
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

    /// <inheritdoc />
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
