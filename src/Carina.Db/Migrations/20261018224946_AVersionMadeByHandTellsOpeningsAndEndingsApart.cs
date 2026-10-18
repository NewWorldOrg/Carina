using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Keeps the first version of four kinds, made by hand, numbered after the highest number kept. It takes the place
/// of the version in use when that one was made by hand too, has never been evaluated and was not chosen by hand,
/// as the first version did when the feature went in; otherwise it is kept as a candidate, and the version in use
/// stays. The version in use changing is what has the detections made again in spare time.
/// </summary>
public partial class AVersionMadeByHandTellsOpeningsAndEndingsApart : Migration
{
    private const string TakeTurn = "SELECT pg_advisory_xact_lock(5243197610103)";

    private const string Made = "kinds = 4 AND evidence IS NULL";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql(
            """
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at)
            SELECT
                coalesce(max(number), 0) + 1,
                'Candidate',
                false,
                4,
                '{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}, "Opening": {"bias": -6.0, "features": {"catalogOpeningShare": 8.0, "pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"catalogEndingShare": 8.0, "pastEndingShare": 4.0}}}}',
                '{}',
                '{}',
                '{"silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}',
                '{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}, "catalogOpeningShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "catalogEndingShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}}',
                now()
            FROM segment_model_version
            """);
        migrationBuilder.Sql(
            """
            UPDATE segment_model_version
            SET state = 'Retired', retired_at = now()
            WHERE state = 'Active' AND NOT by_hand AND kinds = 2 AND evidence IS NULL
            """);
        migrationBuilder.Sql(
            """
            UPDATE segment_model_version
            SET state = 'Active', adopted_at = now()
            WHERE number = (SELECT max(number) FROM segment_model_version)
              AND NOT EXISTS (SELECT 1 FROM segment_model_version WHERE state = 'Active')
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql($"DELETE FROM segment_detection WHERE model_version IN (SELECT number FROM segment_model_version WHERE {Made})");
        migrationBuilder.Sql($"DELETE FROM segment_skip_event WHERE model_version IN (SELECT number FROM segment_model_version WHERE {Made})");
        migrationBuilder.Sql(
            $"""
            UPDATE segment_model_version
            SET state = 'Retired', retired_at = now(), by_hand = false, chosen_by_hand_at = NULL
            WHERE state = 'Active' AND {Made}
            """);
        migrationBuilder.Sql(
            $"""
            UPDATE segment_model_version
            SET state = 'Active', retired_at = NULL
            WHERE kinds = 2 AND evidence IS NULL AND state = 'Retired'
              AND retired_at = (SELECT adopted_at FROM segment_model_version WHERE {Made} AND retired_at = now())
            """);
        migrationBuilder.Sql($"DELETE FROM segment_model_version WHERE {Made}");
    }
}
