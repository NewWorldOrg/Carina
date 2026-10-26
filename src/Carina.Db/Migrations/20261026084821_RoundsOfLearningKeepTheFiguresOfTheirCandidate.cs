using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Keeps with each round of learning that learned a candidate the candidate's own figures and what they stand on, so
/// that what the round said still reads once learning lets go of the candidate. Only columns are added: a round kept
/// before leaves them empty, and nothing is copied or removed. Going back drops them.
/// </summary>
public partial class RoundsOfLearningKeepTheFiguresOfTheirCandidate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "candidate_evidence",
            table: "segment_learning_round",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "candidate_metrics",
            table: "segment_learning_round",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_candidate_figures",
            table: "segment_learning_round",
            sql: "(candidate IS NOT NULL OR (candidate_metrics IS NULL AND candidate_evidence IS NULL))\nAND (candidate_metrics IS NULL OR jsonb_typeof(candidate_metrics) = 'object')\nAND (candidate_evidence IS NULL OR jsonb_typeof(candidate_evidence) = 'object')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_candidate_figures",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "candidate_evidence",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "candidate_metrics",
            table: "segment_learning_round");
    }
}
