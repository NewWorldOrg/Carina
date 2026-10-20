using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Keeps with each round of learning what became of it, the verdict on the candidate it kept and the figures of the
/// version in use that candidate was judged against, so that the status can say why the version in use did or did not
/// change. Rounds kept before leave all three empty, since what became of them was not kept. Going back drops the three.
/// </summary>
public partial class RoundsOfLearningSayWhatBecameOfThem : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "in_use_metrics",
            table: "segment_learning_round",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "outcome",
            table: "segment_learning_round",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "verdict",
            table: "segment_learning_round",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round",
            sql: "(candidate IS NOT NULL OR (verdict IS NULL AND in_use_metrics IS NULL)) AND (in_use_metrics IS NULL OR jsonb_typeof(in_use_metrics) = 'object')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_outcome",
            table: "segment_learning_round",
            sql: "outcome IN ('NothingToLearnFrom', 'Kept', 'Adopted', 'Stopped', 'InUseChangedMeanwhile', 'NothingNew', 'HeldAfterRollback', 'Failed') AND outcome <> 'Stopped'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round",
            sql: "verdict IN ('Adopted', 'ChosenByHand', 'TooLittleEvidence', 'NoBetterAtBoundaries', 'HeldAfterRollback')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_outcome",
            table: "segment_learning_round");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_verdict",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "in_use_metrics",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "outcome",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "verdict",
            table: "segment_learning_round");
    }
}
