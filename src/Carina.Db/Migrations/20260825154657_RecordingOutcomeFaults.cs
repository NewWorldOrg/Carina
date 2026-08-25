using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class RecordingOutcomeFaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_history",
            table: "recording");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_history",
            table: "recording",
            sql: "recording_history_holds(interruptions, resume_count, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow', 'NothingLanded', 'SizeUnobserved', 'StoppedUnasked', 'LighterThanTheStream', 'HeavierThanTheStream']::text[], started_at_actual)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording",
            sql: "recording_reasons_hold(outcome_detail, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow', 'NothingLanded', 'SizeUnobserved', 'StoppedUnasked', 'LighterThanTheStream', 'HeavierThanTheStream']::text[], ARRAY['NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch']::text[], started_at_actual)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_history",
            table: "recording");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_history",
            table: "recording",
            sql: "recording_history_holds(interruptions, resume_count, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow']::text[], started_at_actual)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording",
            sql: "recording_reasons_hold(outcome_detail, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow']::text[], ARRAY['NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch']::text[], started_at_actual)");
    }
}
