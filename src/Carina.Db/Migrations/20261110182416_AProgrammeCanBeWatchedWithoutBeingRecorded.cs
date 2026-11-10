using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AProgrammeCanBeWatchedWithoutBeingRecorded : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_event_kind",
            table: "notification_event");

        migrationBuilder.CreateTable(
            name: "notification_watch",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                programme_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                station_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                lead_minutes = table.Column<int>(type: "integer", nullable: false),
                destinations = table.Column<string>(type: "jsonb", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_watch", x => x.id);
                table.CheckConstraint("ck_notification_watch_broadcast", "ends_at > starts_at");
                table.CheckConstraint("ck_notification_watch_destinations", "jsonb_typeof(destinations) = 'array' AND (state <> 'Waiting' OR jsonb_array_length(destinations) > 0)");
                table.CheckConstraint("ck_notification_watch_lead_minutes", "lead_minutes IN (0, 1, 3, 5, 10)");
                table.CheckConstraint("ck_notification_watch_notified_at", "(state = 'Notified') = (notified_at IS NOT NULL)");
                table.CheckConstraint("ck_notification_watch_state", "state IN ('Waiting', 'Notified', 'Skipped', 'Cancelled')");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_event_kind",
            table: "notification_event",
            sql: "kind IN ('RecordingStarted', 'RecordingCompleted', 'RecordingTruncated', 'RecordingFailed', 'EncodeStarted', 'EncodeCompleted', 'EncodeFailed', 'ReservationCompeting', 'ReservationMissed', 'ProgrammeMoved', 'QualityIncidentDetected', 'QualityIncidentResolved', 'DeviceDisconnected', 'ProgrammeStarting', 'WatchedProgrammeMoved', 'WatchedProgrammeGone')");

        migrationBuilder.CreateIndex(
            name: "ix_notification_watch_state_starts_at",
            table: "notification_watch",
            columns: new[] { "state", "starts_at" });

        migrationBuilder.CreateIndex(
            name: "ux_notification_watch_programme",
            table: "notification_watch",
            columns: new[] { "network_id", "service_id", "event_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_watch");

        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_event_kind",
            table: "notification_event");

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_event_kind",
            table: "notification_event",
            sql: "kind IN ('RecordingStarted', 'RecordingCompleted', 'RecordingTruncated', 'RecordingFailed', 'EncodeStarted', 'EncodeCompleted', 'EncodeFailed', 'ReservationCompeting', 'ReservationMissed', 'ProgrammeMoved', 'QualityIncidentDetected', 'QualityIncidentResolved', 'DeviceDisconnected')");
    }
}
