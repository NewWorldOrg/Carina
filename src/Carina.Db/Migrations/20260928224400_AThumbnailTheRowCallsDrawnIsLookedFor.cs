using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AThumbnailTheRowCallsDrawnIsLookedFor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_fault",
            table: "integrity_finding");

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_recording",
            table: "integrity_finding");

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_fault",
            table: "integrity_finding",
            sql: "fault IN ('SizeDisagrees', 'NoLedgerRow', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete', 'ThumbnailMissing')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_recording",
            table: "integrity_finding",
            sql: "(fault IN ('SizeDisagrees', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete', 'ThumbnailMissing')) = (recording_id IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DELETE FROM integrity_finding WHERE fault = 'ThumbnailMissing'");

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_fault",
            table: "integrity_finding");

        migrationBuilder.DropCheckConstraint(
            name: "ck_integrity_finding_recording",
            table: "integrity_finding");

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_fault",
            table: "integrity_finding",
            sql: "fault IN ('SizeDisagrees', 'NoLedgerRow', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_integrity_finding_recording",
            table: "integrity_finding",
            sql: "(fault IN ('SizeDisagrees', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete')) = (recording_id IS NOT NULL)");
    }
}
