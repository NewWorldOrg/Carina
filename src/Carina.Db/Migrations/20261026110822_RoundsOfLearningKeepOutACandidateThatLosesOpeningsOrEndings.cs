using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets a round of learning say it kept out a candidate that finds the boundaries better but loses openings or endings.
/// Only the verdicts a row may hold widen: nothing is copied. Going back leaves such a round without a verdict, which
/// a round that kept a candidate may be, since the verdicts before this one cannot say it.
/// </summary>
public partial class RoundsOfLearningKeepOutACandidateThatLosesOpeningsOrEndings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round",
            sql: "verdict IN ('Adopted', 'ChosenByHand', 'TooLittleEvidence', 'NoBetterAtBoundaries', 'HeldAfterRollback', 'LosesOpeningsOrEndings')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round");

        migrationBuilder.Sql("UPDATE segment_learning_round SET verdict = NULL WHERE verdict = 'LosesOpeningsOrEndings'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round",
            sql: "verdict IN ('Adopted', 'ChosenByHand', 'TooLittleEvidence', 'NoBetterAtBoundaries', 'HeldAfterRollback')");
    }
}
