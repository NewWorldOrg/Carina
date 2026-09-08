using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ThumbnailsAmongTheThingsLeftAlone : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR affected IS NULL)\nAND (subject <> 'DuplicateAvoidance' OR affected IS NOT NULL)\nAND (subject <> 'QualityTimeSeries' OR affected IS NULL)\nAND (subject <> 'RecordingHistory' OR affected IS NULL)\nAND (subject <> 'EnclosedCharacters' OR affected IS NOT NULL)\nAND (subject <> 'Thumbnails' OR affected IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'EnclosedCharacters' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'Thumbnails' OR ground = 'NotMigratedByDesign')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission",
            sql: "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory', 'EnclosedCharacters', 'Thumbnails')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR affected IS NULL)\nAND (subject <> 'DuplicateAvoidance' OR affected IS NOT NULL)\nAND (subject <> 'QualityTimeSeries' OR affected IS NULL)\nAND (subject <> 'RecordingHistory' OR affected IS NULL)\nAND (subject <> 'EnclosedCharacters' OR affected IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'EnclosedCharacters' OR ground = 'NotMigratedByDesign')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission",
            sql: "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory', 'EnclosedCharacters')");
    }
}
