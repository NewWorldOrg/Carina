using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TryingAgainWhileItIsStillOn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.AddColumn<string>(
            name: "gave_up_because",
            table: "reservation_outcome",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "retry_result",
            table: "reservation_outcome",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome",
            columns: new[] { "reservation_id", "kind" },
            unique: true,
            filter: "kind <> 'Retried'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_gave_up",
            table: "reservation_outcome",
            sql: "(gave_up_because IS NULL OR gave_up_because IN ('NotTransient', 'PrecheckFailed', 'CandidateNeedsAttention', 'BroadcastOver', 'AttemptsSpent'))\nAND (kind = 'GaveUpRetrying') = (gave_up_because IS NOT NULL)\nAND (kind <> 'GaveUpRetrying'\n     OR (gave_up_because = 'NotTransient') = (tune_failure IS NOT NULL))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone', 'ProgrammeReturned', 'Retried', 'GaveUpRetrying')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_retry",
            table: "reservation_outcome",
            sql: "(retry_result IS NULL OR retry_result IN ('Started', 'RefusedAgain', 'NoAnswer'))\nAND (kind = 'Retried') = (retry_result IS NOT NULL)\nAND (retry_result IS DISTINCT FROM 'Started'\n     OR (tune_failure IS NULL AND jsonb_array_length(faults) = 0))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_gave_up",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_retry",
            table: "reservation_outcome");

        migrationBuilder.DropColumn(
            name: "gave_up_because",
            table: "reservation_outcome");

        migrationBuilder.DropColumn(
            name: "retry_result",
            table: "reservation_outcome");

        migrationBuilder.CreateIndex(
            name: "ux_reservation_outcome_reservation_kind",
            table: "reservation_outcome",
            columns: new[] { "reservation_id", "kind" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_kind",
            table: "reservation_outcome",
            sql: "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure', 'ProgrammeMoved', 'ProgrammeGone', 'ProgrammeReturned')");
    }
}
