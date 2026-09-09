using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheLossCarriedIntoTheNewSystem : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "migration_omission");

        migrationBuilder.CreateTable(
            name: "migration_loss",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                affected = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_loss", x => new { x.run_id, x.subject });
                table.CheckConstraint("ck_migration_loss_affected", "affected >= 0");
                table.CheckConstraint("ck_migration_loss_subject", "subject IN ('DuplicateAvoidance', 'EnclosedCharacters')");
                table.ForeignKey(
                    name: "fk_migration_loss_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "migration_loss");

        migrationBuilder.CreateTable(
            name: "migration_omission",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                affected = table.Column<int>(type: "integer", nullable: true),
                ground = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_omission", x => new { x.run_id, x.subject });
                table.CheckConstraint("ck_migration_omission_affected", "(subject <> 'ProgrammeGuide' OR affected IS NULL)\nAND (subject <> 'DuplicateAvoidance' OR affected IS NOT NULL)\nAND (subject <> 'QualityTimeSeries' OR affected IS NULL)\nAND (subject <> 'RecordingHistory' OR affected IS NULL)\nAND (subject <> 'EnclosedCharacters' OR affected IS NOT NULL)\nAND (subject <> 'Thumbnails' OR affected IS NULL)");
                table.CheckConstraint("ck_migration_omission_affected_counts", "affected IS NULL OR affected >= 0");
                table.CheckConstraint("ck_migration_omission_ground", "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'EnclosedCharacters' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'Thumbnails' OR ground = 'NotMigratedByDesign')");
                table.CheckConstraint("ck_migration_omission_subject", "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory', 'EnclosedCharacters', 'Thumbnails')");
                table.ForeignKey(
                    name: "fk_migration_omission_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }
}
