using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AProgrammeCarriesTheMarksOfItsNameAndSummary : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "marks",
            table: "programme",
            type: "text[]",
            nullable: true,
            computedColumnSql: "array_remove(ARRAY[CASE WHEN strpos(name, chr(127306)) > 0 OR strpos(summary, chr(127306)) > 0 THEN 'HighDefinition' END, CASE WHEN strpos(name, chr(127308)) > 0 OR strpos(summary, chr(127308)) > 0 THEN 'StandardDefinition' END, CASE WHEN strpos(name, chr(127295)) > 0 OR strpos(summary, chr(127295)) > 0 THEN 'Progressive' END, CASE WHEN strpos(name, chr(127302)) > 0 OR strpos(summary, chr(127302)) > 0 THEN 'Widescreen' END, CASE WHEN strpos(name, chr(127307)) > 0 OR strpos(summary, chr(127307)) > 0 THEN 'MultiView' END, CASE WHEN strpos(name, chr(127504)) > 0 OR strpos(summary, chr(127504)) > 0 THEN 'SignLanguage' END, CASE WHEN strpos(name, chr(127505)) > 0 OR strpos(summary, chr(127505)) > 0 THEN 'Captioned' END, CASE WHEN strpos(name, chr(127506)) > 0 OR strpos(summary, chr(127506)) > 0 THEN 'Interactive' END, CASE WHEN strpos(name, chr(127507)) > 0 OR strpos(summary, chr(127507)) > 0 THEN 'DataBroadcast' END, CASE WHEN strpos(name, chr(127298)) > 0 OR strpos(summary, chr(127298)) > 0 THEN 'Stereo' END, CASE WHEN strpos(name, chr(127508)) > 0 OR strpos(summary, chr(127508)) > 0 THEN 'Bilingual' END, CASE WHEN strpos(name, chr(127509)) > 0 OR strpos(summary, chr(127509)) > 0 THEN 'MultipleAudio' END, CASE WHEN strpos(name, chr(127510)) > 0 OR strpos(summary, chr(127510)) > 0 THEN 'AudioDescription' END, CASE WHEN strpos(name, chr(127309)) > 0 OR strpos(summary, chr(127309)) > 0 THEN 'SurroundStereo' END, CASE WHEN strpos(name, chr(127281)) > 0 OR strpos(summary, chr(127281)) > 0 THEN 'BModeStereo' END, CASE WHEN strpos(name, chr(127293)) > 0 OR strpos(summary, chr(127293)) > 0 THEN 'News' END, CASE WHEN strpos(name, chr(127511)) > 0 OR strpos(summary, chr(127511)) > 0 THEN 'Weather' END, CASE WHEN strpos(name, chr(127512)) > 0 OR strpos(summary, chr(127512)) > 0 THEN 'Traffic' END, CASE WHEN strpos(name, chr(127513)) > 0 OR strpos(summary, chr(127513)) > 0 THEN 'Film' END, CASE WHEN strpos(name, chr(127514)) > 0 OR strpos(summary, chr(127514)) > 0 THEN 'Free' END, CASE WHEN strpos(name, chr(127515)) > 0 OR strpos(summary, chr(127515)) > 0 THEN 'Paid' END, CASE WHEN strpos(name, chr(9919)) > 0 OR strpos(summary, chr(9919)) > 0 THEN 'ParentalLock' END, CASE WHEN strpos(name, chr(127516)) > 0 OR strpos(summary, chr(127516)) > 0 THEN 'FirstPart' END, CASE WHEN strpos(name, chr(127517)) > 0 OR strpos(summary, chr(127517)) > 0 THEN 'SecondPart' END, CASE WHEN strpos(name, chr(127518)) > 0 OR strpos(summary, chr(127518)) > 0 THEN 'Rerun' END, CASE WHEN strpos(name, chr(127519)) > 0 OR strpos(summary, chr(127519)) > 0 THEN 'New' END, CASE WHEN strpos(name, chr(127520)) > 0 OR strpos(summary, chr(127520)) > 0 THEN 'Premiere' END, CASE WHEN strpos(name, chr(127521)) > 0 OR strpos(summary, chr(127521)) > 0 THEN 'Final' END, CASE WHEN strpos(name, chr(127522)) > 0 OR strpos(summary, chr(127522)) > 0 THEN 'Live' END, CASE WHEN strpos(name, chr(127523)) > 0 OR strpos(summary, chr(127523)) > 0 THEN 'Shopping' END, CASE WHEN strpos(name, chr(127524)) > 0 OR strpos(summary, chr(127524)) > 0 THEN 'VoiceCast' END, CASE WHEN strpos(name, chr(127525)) > 0 OR strpos(summary, chr(127525)) > 0 THEN 'Dubbed' END, CASE WHEN strpos(name, chr(127310)) > 0 OR strpos(summary, chr(127310)) > 0 THEN 'PayPerView' END]::text[], NULL)",
            stored: true);

        migrationBuilder.AddColumn<string[]>(
            name: "marks",
            table: "archived_programme",
            type: "text[]",
            nullable: true,
            computedColumnSql: "array_remove(ARRAY[CASE WHEN strpos(name, chr(127306)) > 0 OR strpos(summary, chr(127306)) > 0 THEN 'HighDefinition' END, CASE WHEN strpos(name, chr(127308)) > 0 OR strpos(summary, chr(127308)) > 0 THEN 'StandardDefinition' END, CASE WHEN strpos(name, chr(127295)) > 0 OR strpos(summary, chr(127295)) > 0 THEN 'Progressive' END, CASE WHEN strpos(name, chr(127302)) > 0 OR strpos(summary, chr(127302)) > 0 THEN 'Widescreen' END, CASE WHEN strpos(name, chr(127307)) > 0 OR strpos(summary, chr(127307)) > 0 THEN 'MultiView' END, CASE WHEN strpos(name, chr(127504)) > 0 OR strpos(summary, chr(127504)) > 0 THEN 'SignLanguage' END, CASE WHEN strpos(name, chr(127505)) > 0 OR strpos(summary, chr(127505)) > 0 THEN 'Captioned' END, CASE WHEN strpos(name, chr(127506)) > 0 OR strpos(summary, chr(127506)) > 0 THEN 'Interactive' END, CASE WHEN strpos(name, chr(127507)) > 0 OR strpos(summary, chr(127507)) > 0 THEN 'DataBroadcast' END, CASE WHEN strpos(name, chr(127298)) > 0 OR strpos(summary, chr(127298)) > 0 THEN 'Stereo' END, CASE WHEN strpos(name, chr(127508)) > 0 OR strpos(summary, chr(127508)) > 0 THEN 'Bilingual' END, CASE WHEN strpos(name, chr(127509)) > 0 OR strpos(summary, chr(127509)) > 0 THEN 'MultipleAudio' END, CASE WHEN strpos(name, chr(127510)) > 0 OR strpos(summary, chr(127510)) > 0 THEN 'AudioDescription' END, CASE WHEN strpos(name, chr(127309)) > 0 OR strpos(summary, chr(127309)) > 0 THEN 'SurroundStereo' END, CASE WHEN strpos(name, chr(127281)) > 0 OR strpos(summary, chr(127281)) > 0 THEN 'BModeStereo' END, CASE WHEN strpos(name, chr(127293)) > 0 OR strpos(summary, chr(127293)) > 0 THEN 'News' END, CASE WHEN strpos(name, chr(127511)) > 0 OR strpos(summary, chr(127511)) > 0 THEN 'Weather' END, CASE WHEN strpos(name, chr(127512)) > 0 OR strpos(summary, chr(127512)) > 0 THEN 'Traffic' END, CASE WHEN strpos(name, chr(127513)) > 0 OR strpos(summary, chr(127513)) > 0 THEN 'Film' END, CASE WHEN strpos(name, chr(127514)) > 0 OR strpos(summary, chr(127514)) > 0 THEN 'Free' END, CASE WHEN strpos(name, chr(127515)) > 0 OR strpos(summary, chr(127515)) > 0 THEN 'Paid' END, CASE WHEN strpos(name, chr(9919)) > 0 OR strpos(summary, chr(9919)) > 0 THEN 'ParentalLock' END, CASE WHEN strpos(name, chr(127516)) > 0 OR strpos(summary, chr(127516)) > 0 THEN 'FirstPart' END, CASE WHEN strpos(name, chr(127517)) > 0 OR strpos(summary, chr(127517)) > 0 THEN 'SecondPart' END, CASE WHEN strpos(name, chr(127518)) > 0 OR strpos(summary, chr(127518)) > 0 THEN 'Rerun' END, CASE WHEN strpos(name, chr(127519)) > 0 OR strpos(summary, chr(127519)) > 0 THEN 'New' END, CASE WHEN strpos(name, chr(127520)) > 0 OR strpos(summary, chr(127520)) > 0 THEN 'Premiere' END, CASE WHEN strpos(name, chr(127521)) > 0 OR strpos(summary, chr(127521)) > 0 THEN 'Final' END, CASE WHEN strpos(name, chr(127522)) > 0 OR strpos(summary, chr(127522)) > 0 THEN 'Live' END, CASE WHEN strpos(name, chr(127523)) > 0 OR strpos(summary, chr(127523)) > 0 THEN 'Shopping' END, CASE WHEN strpos(name, chr(127524)) > 0 OR strpos(summary, chr(127524)) > 0 THEN 'VoiceCast' END, CASE WHEN strpos(name, chr(127525)) > 0 OR strpos(summary, chr(127525)) > 0 THEN 'Dubbed' END, CASE WHEN strpos(name, chr(127310)) > 0 OR strpos(summary, chr(127310)) > 0 THEN 'PayPerView' END]::text[], NULL)",
            stored: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "marks",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "marks",
            table: "archived_programme");
    }
}
