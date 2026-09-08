using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class MigrationRuleAndChannelProposals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission");

        migrationBuilder.AddColumn<int>(
            name: "affected",
            table: "migration_omission",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "migration_channel_proposal",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                standing = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                source_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                source_physical_channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                rescanned_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_channel_proposal", x => new { x.run_id, x.network_id, x.service_id });
                table.CheckConstraint("ck_migration_channel_proposal_name", "(standing = 'NameProposed') = (rescanned_name IS NOT NULL)");
                table.CheckConstraint("ck_migration_channel_proposal_standing", "standing IN ('NameProposed', 'NothingAnswers', 'Inexpressible')");
                table.ForeignKey(
                    name: "fk_migration_channel_proposal_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "migration_rule_proposal",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_row = table.Column<long>(type: "bigint", nullable: false),
                rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                enabled_at_the_source = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_rule_proposal", x => new { x.run_id, x.source_row });
                table.CheckConstraint("ck_migration_rule_proposal_source_row", "source_row > 0");
                table.ForeignKey(
                    name: "fk_migration_rule_proposal_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR affected IS NULL)\nAND (subject <> 'DuplicateAvoidance' OR affected IS NOT NULL)\nAND (subject <> 'QualityTimeSeries' OR affected IS NULL)\nAND (subject <> 'RecordingHistory' OR affected IS NULL)\nAND (subject <> 'EnclosedCharacters' OR affected IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_affected_counts",
            table: "migration_omission",
            sql: "affected IS NULL OR affected >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'EnclosedCharacters' OR ground = 'NotMigratedByDesign')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission",
            sql: "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory', 'EnclosedCharacters')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "migration_channel_proposal");

        migrationBuilder.DropTable(
            name: "migration_rule_proposal");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_affected",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_affected_counts",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission");

        migrationBuilder.DropColumn(
            name: "affected",
            table: "migration_omission");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_ground",
            table: "migration_omission",
            sql: "(subject <> 'ProgrammeGuide' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'DuplicateAvoidance' OR ground = 'NotMigratedByDesign')\nAND (subject <> 'QualityTimeSeries' OR ground = 'NothingToCarry')\nAND (subject <> 'RecordingHistory' OR ground = 'NotMigratedByDesign')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_omission_subject",
            table: "migration_omission",
            sql: "subject IN ('ProgrammeGuide', 'DuplicateAvoidance', 'QualityTimeSeries', 'RecordingHistory')");
    }
}
