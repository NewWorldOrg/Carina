using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Integrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "integrity_check",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                roots_walked = table.Column<int>(type: "integer", nullable: false),
                roots_out_of_reach = table.Column<int>(type: "integer", nullable: false),
                files_read = table.Column<int>(type: "integer", nullable: false),
                ledger_rows_read = table.Column<int>(type: "integer", nullable: false),
                ledger_rows_judged = table.Column<int>(type: "integer", nullable: false),
                ledger_rows_still_writing = table.Column<int>(type: "integer", nullable: false),
                ledger_rows_in_roots_out_of_reach = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_integrity_check", x => x.id);
                table.CheckConstraint("ck_integrity_check_counts", "roots_walked >= 0\nAND roots_out_of_reach >= 0\nAND files_read >= 0\nAND ledger_rows_read >= 0\nAND ledger_rows_judged >= 0\nAND ledger_rows_still_writing >= 0\nAND ledger_rows_in_roots_out_of_reach >= 0");
                table.CheckConstraint("ck_integrity_check_span", "finished_at >= started_at");
            });

        migrationBuilder.CreateTable(
            name: "integrity_finding",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                check_id = table.Column<Guid>(type: "uuid", nullable: false),
                fault = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                path = table.Column<string>(type: "text", nullable: false),
                recording_id = table.Column<Guid>(type: "uuid", nullable: true),
                ledger_size = table.Column<long>(type: "bigint", nullable: true),
                observed_size = table.Column<long>(type: "bigint", nullable: true),
                noticed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_integrity_finding", x => x.id);
                table.CheckConstraint("ck_integrity_finding_fault", "fault IN ('SizeDisagrees', 'NoLedgerRow', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete')");
                table.CheckConstraint("ck_integrity_finding_ledger_size", "(fault IN ('SizeDisagrees', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete')) = (ledger_size IS NOT NULL)");
                table.CheckConstraint("ck_integrity_finding_observed_size", "(fault IN ('SizeDisagrees', 'NoLedgerRow', 'FileEmpty', 'EmptyThoughComplete')) = (observed_size IS NOT NULL)");
                table.CheckConstraint("ck_integrity_finding_path", "length(path) > 0 AND left(path, 1) <> '/'");
                table.CheckConstraint("ck_integrity_finding_recording", "(fault IN ('SizeDisagrees', 'FileMissing', 'FileEmpty', 'EmptyThoughComplete')) = (recording_id IS NOT NULL)");
                table.CheckConstraint("ck_integrity_finding_sizes", "(ledger_size IS NULL OR ledger_size >= 0) AND (observed_size IS NULL OR observed_size >= 0)");
                table.ForeignKey(
                    name: "fk_integrity_finding_integrity_check_check_id",
                    column: x => x.check_id,
                    principalTable: "integrity_check",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_integrity_check_finished",
            table: "integrity_check",
            column: "finished_at");

        migrationBuilder.CreateIndex(
            name: "ix_integrity_finding_check",
            table: "integrity_finding",
            columns: new[] { "check_id", "fault" });

        migrationBuilder.CreateIndex(
            name: "ix_integrity_finding_recording",
            table: "integrity_finding",
            column: "recording_id",
            filter: "recording_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "integrity_finding");

        migrationBuilder.DropTable(
            name: "integrity_check");
    }
}
