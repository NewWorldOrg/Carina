using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheArchiveKeepsWhichProgrammesWereSuperseded : Migration
{
    private const string JudgeWhatIsAlreadyKept = """
        WITH sharing AS (
            SELECT
                ordered.network_id,
                ordered.service_id,
                ordered.event_id,
                ordered.start_at,
                ordered.ends,
                ordered.kept,
                CASE
                    WHEN ordered.kept THEN
                        (SELECT heard.last_heard_at
                         FROM archived_programme AS heard
                         WHERE heard.network_id = ordered.network_id
                           AND heard.service_id = ordered.service_id
                           AND heard.event_id = ordered.event_id
                           AND heard.start_at = ordered.start_at)
                    ELSE
                        (SELECT heard.last_heard_at
                         FROM programme AS heard
                         WHERE heard.network_id = ordered.network_id
                           AND heard.service_id = ordered.service_id
                           AND heard.event_id = ordered.event_id)
                END AS last_heard_at
            FROM (
                SELECT
                    network_id,
                    service_id,
                    event_id,
                    start_at,
                    ends,
                    kept,
                    max(ends) OVER earlier AS earlier_end,
                    lead(start_at) OVER earlier AS next_start
                FROM (
                    SELECT
                        network_id,
                        service_id,
                        event_id,
                        start_at,
                        COALESCE(end_at, start_at + INTERVAL '3600 seconds') AS ends,
                        false AS kept
                    FROM programme
                    WHERE NOT is_shadow
                    UNION ALL
                    SELECT network_id, service_id, event_id, start_at, end_at, true
                    FROM archived_programme) AS aired
                WINDOW earlier AS (
                    PARTITION BY network_id, service_id
                    ORDER BY start_at, event_id
                    ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING)) AS ordered
            WHERE ordered.earlier_end > ordered.start_at OR ordered.next_start < ordered.ends)
        UPDATE archived_programme AS archived
        SET superseded = true
        FROM sharing AS older
        WHERE older.kept
          AND archived.network_id = older.network_id
          AND archived.service_id = older.service_id
          AND archived.event_id = older.event_id
          AND archived.start_at = older.start_at
          AND EXISTS (
            SELECT 1
            FROM sharing AS newer
            WHERE newer.network_id = older.network_id
              AND newer.service_id = older.service_id
              AND newer.last_heard_at > COALESCE(older.last_heard_at, '-infinity'::timestamptz)
              AND newer.start_at < older.ends
              AND newer.ends > older.start_at);
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme");

        migrationBuilder.AddColumn<bool>(
            name: "superseded",
            table: "archived_programme",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme",
            column: "end_at")
            .Annotation("Npgsql:IndexInclude", new[] { "network_id", "service_id", "event_id", "start_at", "superseded" });

        migrationBuilder.Sql(JudgeWhatIsAlreadyKept);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme");

        migrationBuilder.DropColumn(
            name: "superseded",
            table: "archived_programme");

        migrationBuilder.CreateIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme",
            column: "end_at")
            .Annotation("Npgsql:IndexInclude", new[] { "network_id", "service_id", "event_id", "start_at" });
    }
}
