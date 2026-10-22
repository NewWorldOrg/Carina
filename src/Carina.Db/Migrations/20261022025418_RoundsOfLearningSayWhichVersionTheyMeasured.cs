using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Keeps with each round of learning the number of the version in use it measured, so that the figures the status
/// answers for a version never evaluated itself are those of a round that measured that version, however long ago and
/// whatever was put in use or rolled back meanwhile. A round kept before with figures of the version in use gets the
/// number of the one version that was in use when it was taken; it is left empty when none or more than one was, or when
/// a version was rolled back from within an hour of it, since that version was then put in use after it was taken. Going
/// back drops the number.
/// </summary>
public partial class RoundsOfLearningSayWhichVersionTheyMeasured : Migration
{
    private const string InUseWhenTaken =
        "version.adopted_at <= rounds.taken_at AND (version.retired_at IS NULL OR version.retired_at > rounds.taken_at)";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round");

        migrationBuilder.AddColumn<int>(
            name: "in_use_version",
            table: "segment_learning_round",
            type: "integer",
            nullable: true);

        migrationBuilder.Sql(
            $"""
            UPDATE segment_learning_round AS rounds
            SET in_use_version = (SELECT min(version.number) FROM segment_model_version AS version WHERE {InUseWhenTaken})
            WHERE rounds.in_use_metrics IS NOT NULL
              AND (SELECT count(*) FROM segment_model_version AS version WHERE {InUseWhenTaken}) = 1
              AND NOT EXISTS (
                  SELECT 1 FROM segment_model_version AS left_behind
                  WHERE left_behind.rolled_back_at >= rounds.taken_at AND left_behind.rolled_back_at < rounds.taken_at + interval '1 hour')
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_in_use_version",
            table: "segment_learning_round",
            sql: "in_use_version >= 1");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round",
            sql: "(candidate IS NOT NULL OR (verdict IS NULL AND in_use_metrics IS NULL AND in_use_version IS NULL)) AND (in_use_metrics IS NULL OR jsonb_typeof(in_use_metrics) = 'object')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_in_use_version",
            table: "segment_learning_round");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round");

        migrationBuilder.DropColumn(
            name: "in_use_version",
            table: "segment_learning_round");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_round_judged",
            table: "segment_learning_round",
            sql: "(candidate IS NOT NULL OR (verdict IS NULL AND in_use_metrics IS NULL)) AND (in_use_metrics IS NULL OR jsonb_typeof(in_use_metrics) = 'object')");
    }
}
