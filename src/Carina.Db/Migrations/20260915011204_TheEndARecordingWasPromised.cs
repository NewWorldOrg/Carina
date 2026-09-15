using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheEndARecordingWasPromised : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<DateTime>(
            name: "promised_window_end",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE recording SET promised_window_end = expected_window_end;");

        migrationBuilder.AlterColumn<DateTime>(
            name: "promised_window_end",
            table: "recording",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_window_promised",
            table: "recording",
            sql: "promised_window_end > expected_window_start AND promised_window_end <= expected_window_end");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_window_promised",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "promised_window_end",
            table: "recording");
    }
}
