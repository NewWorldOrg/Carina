using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class NoticesAreLedgeredAndSentToWebhooks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_destination",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                sealed_url = table.Column<byte[]>(type: "bytea", nullable: false),
                url_tail = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                last_result_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_destination", x => x.id);
                table.CheckConstraint("ck_notification_destination_last_failure", "(last_failure IS NULL OR last_failure IN ('TimedOut', 'RateLimited', 'ServerFailed', 'Unreachable', 'Rejected', 'DestinationDisabled', 'Unreadable', 'TooOld', 'TlsFailed'))\nAND (last_failure IS NULL OR last_result_at IS NOT NULL)");
                table.CheckConstraint("ck_notification_destination_name", "btrim(name) = name AND length(name) > 0");
                table.CheckConstraint("ck_notification_destination_sealed_url", "octet_length(sealed_url) BETWEEN 1 AND 4096");
                table.CheckConstraint("ck_notification_destination_target", "target IN ('Slack', 'Discord')");
                table.CheckConstraint("ck_notification_destination_url_tail", "length(url_tail) = 4");
            });

        migrationBuilder.CreateTable(
            name: "notification_event",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                tone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                link = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                taken_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_event", x => x.id);
                table.CheckConstraint("ck_notification_event_kind", "kind IN ('RecordingStarted', 'RecordingCompleted', 'RecordingTruncated', 'RecordingFailed', 'EncodeStarted', 'EncodeCompleted', 'EncodeFailed', 'ReservationCompeting', 'ReservationMissed', 'ProgrammeMoved', 'QualityIncidentDetected', 'QualityIncidentResolved', 'DeviceDisconnected')");
                table.CheckConstraint("ck_notification_event_tone", "tone IN ('Good', 'Informative', 'Caution', 'Failure')");
                table.CheckConstraint("ck_notification_event_words", "length(btrim(title)) > 0 AND length(btrim(body)) > 0 AND (link IS NULL OR link ~ '^https?://')");
            });

        migrationBuilder.CreateTable(
            name: "notification_rule",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                events = table.Column<string>(type: "jsonb", nullable: false),
                destinations = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_rule", x => x.id);
                table.CheckConstraint("ck_notification_rule_destinations", "jsonb_typeof(destinations) = 'array' AND jsonb_array_length(destinations) > 0");
                table.CheckConstraint("ck_notification_rule_events", "jsonb_typeof(events) = 'array' AND events <@ '[\"RecordingStarted\", \"RecordingCompleted\", \"RecordingTruncated\", \"RecordingFailed\", \"EncodeStarted\", \"EncodeCompleted\", \"EncodeFailed\", \"ReservationCompeting\", \"ReservationMissed\", \"ProgrammeMoved\", \"QualityIncidentDetected\", \"QualityIncidentResolved\", \"DeviceDisconnected\"]'::jsonb");
                table.CheckConstraint("ck_notification_rule_name", "btrim(name) = name AND length(name) > 0");
            });

        migrationBuilder.CreateTable(
            name: "notification_settings",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                notify_lead_minutes = table.Column<int>(type: "integer", nullable: false),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_settings", x => x.id);
                table.CheckConstraint("ck_notification_settings_notify_lead_minutes", "notify_lead_minutes IN (0, 1, 3, 5, 10)");
                table.CheckConstraint("ck_notification_settings_single_row", "id = 1");
            });

        migrationBuilder.CreateTable(
            name: "notification_delivery",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_delivery", x => x.id);
                table.CheckConstraint("ck_notification_delivery_attempts", "attempts BETWEEN 0 AND 4");
                table.CheckConstraint("ck_notification_delivery_last_failure", "last_failure IS NULL OR last_failure IN ('TimedOut', 'RateLimited', 'ServerFailed', 'Unreachable', 'Rejected', 'DestinationDisabled', 'Unreadable', 'TooOld', 'TlsFailed')");
                table.CheckConstraint("ck_notification_delivery_standing", "(state <> 'Queued'\n OR (attempts = 0 AND next_attempt_at IS NOT NULL AND sent_at IS NULL AND last_failure IS NULL))\nAND (state <> 'Failed'\n     OR (attempts > 0 AND next_attempt_at IS NOT NULL AND sent_at IS NULL AND last_failure IS NOT NULL))\nAND (state <> 'Sent'\n     OR (attempts > 0 AND next_attempt_at IS NULL AND sent_at IS NOT NULL AND last_failure IS NULL))\nAND (state <> 'GivenUp'\n     OR (next_attempt_at IS NULL AND sent_at IS NULL AND last_failure IS NOT NULL))");
                table.CheckConstraint("ck_notification_delivery_state", "state IN ('Queued', 'Sent', 'Failed', 'GivenUp')");
                table.ForeignKey(
                    name: "fk_notification_delivery_notification_destination_destination_",
                    column: x => x.destination_id,
                    principalTable: "notification_destination",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_notification_delivery_notification_event_event_id",
                    column: x => x.event_id,
                    principalTable: "notification_event",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notification_delivery_destination_id",
            table: "notification_delivery",
            column: "destination_id");

        migrationBuilder.CreateIndex(
            name: "ix_notification_delivery_next_attempt_at",
            table: "notification_delivery",
            column: "next_attempt_at",
            filter: "next_attempt_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_notification_delivery_event_destination",
            table: "notification_delivery",
            columns: ["event_id", "destination_id"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_notification_event_occurred_at",
            table: "notification_event",
            column: "occurred_at");

        migrationBuilder.CreateIndex(
            name: "ux_notification_event_source_kind",
            table: "notification_event",
            columns: ["source_id", "kind"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_delivery");

        migrationBuilder.DropTable(
            name: "notification_rule");

        migrationBuilder.DropTable(
            name: "notification_settings");

        migrationBuilder.DropTable(
            name: "notification_destination");

        migrationBuilder.DropTable(
            name: "notification_event");
    }
}
