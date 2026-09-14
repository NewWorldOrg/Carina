using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TakingBackWhatWasLeftRunning : Migration
{
    private const string Breaking =
        "'TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', "
        + "'StoppedByHand', 'TunerContended', 'ScramblingUnresolved'";

    private const string Faults =
        Breaking
        + ", 'ShortOfTheWindow', 'NothingLanded', 'SizeUnobserved', 'StoppedUnasked', "
        + "'LighterThanTheStream', 'HeavierThanTheStream', 'EndStillUndecided'";

    private const string FaultsAsJson =
        "\"TuneFailed\", \"RefusedByDiskPrecheck\", \"DiskExhausted\", \"DriverLost\", \"DrainGraceExpired\", "
        + "\"StoppedByHand\", \"TunerContended\", \"ScramblingUnresolved\", \"ShortOfTheWindow\", \"NothingLanded\", "
        + "\"SizeUnobserved\", \"StoppedUnasked\", \"LighterThanTheStream\", \"HeavierThanTheStream\", "
        + "\"EndStillUndecided\"";

    private const string NothingWasWritingIt = ", 'LeftRunningUnwatched', 'DriverReplaced'";

    private const string NothingWasWritingItAsJson = ", \"LeftRunningUnwatched\", \"DriverReplaced\"";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        Reasons(migrationBuilder, NothingWasWritingIt, NothingWasWritingItAsJson);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        Reasons(migrationBuilder, string.Empty, string.Empty);
    }

    private static void Reasons(MigrationBuilder migrationBuilder, string added, string addedAsJson)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_history",
            table: "recording");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome",
            sql: $"faults <@ '[{FaultsAsJson}{addedAsJson}]'::jsonb\n"
                 + "AND (kind <> 'TuneFailure' OR faults @> '[\"TuneFailed\"]'::jsonb)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_history",
            table: "recording",
            sql: $"recording_history_holds(interruptions, resume_count, ARRAY[{Breaking}{added}]::text[], "
                 + "started_at_actual)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording",
            sql: $"recording_reasons_hold(outcome_detail, ARRAY[{Faults}{added}]::text[], "
                 + "ARRAY['NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch']::text[], started_at_actual)");
    }
}
