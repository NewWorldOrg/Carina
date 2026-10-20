using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets a catalog entry's kind be decided by the opening or ending it carries on, and gives the copy of a programme
/// kept with a recording's learning data the end its recording was promised, or else the one it had, where the
/// guide's end was not known and the recording is still kept. Going back takes every entry carried on so for one
/// not known, to be estimated again; the ends given stay, as nothing tells them from the guide's.
/// </summary>
public partial class CatalogEntriesCarryOnOpeningsAndCopiesOfProgrammesEndWhereTheirRecordingsWerePromised : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry",
            sql: "(decided_by IS NULL) = (kind = 'Unknown') AND (decided_by IS NULL OR decided_by IN ('Pattern', 'Continuation'))");

        migrationBuilder.Sql(
            """
            UPDATE segment_extraction AS extraction
            SET programme_ends_at = coalesce(recording.promised_window_end, recording.expected_window_end)
            FROM recording
            WHERE recording.id = extraction.recording_id
              AND extraction.programme_ends_at IS NULL
              AND coalesce(recording.promised_window_end, recording.expected_window_end) > extraction.programme_starts_at
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            UPDATE segment_catalog_entry
            SET kind = 'Unknown', confidence = NULL, decided_by = NULL, kinds_version = 0
            WHERE decided_by = 'Continuation'
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry",
            sql: "(decided_by IS NULL) = (kind = 'Unknown') AND (decided_by IS NULL OR decided_by IN ('Pattern'))");
    }
}
