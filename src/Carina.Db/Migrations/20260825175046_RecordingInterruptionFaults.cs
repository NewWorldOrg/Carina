using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class RecordingInterruptionFaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_history",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_history",
            table: "recording",
            sql: "recording_history_holds(interruptions, resume_count, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved']::text[], started_at_actual)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_history",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_history",
            table: "recording",
            sql: "recording_history_holds(interruptions, resume_count, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow', 'NothingLanded', 'SizeUnobserved', 'StoppedUnasked', 'LighterThanTheStream', 'HeavierThanTheStream']::text[], started_at_actual)");
    }
}
