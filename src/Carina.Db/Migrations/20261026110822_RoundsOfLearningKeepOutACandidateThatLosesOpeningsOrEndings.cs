using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class RoundsOfLearningKeepOutACandidateThatLosesOpeningsOrEndings : Migration
{
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
