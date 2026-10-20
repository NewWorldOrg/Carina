using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Keeps the version of four kinds made by hand that weighs as the one before it and hears silence only from -55 dBFS,
/// below the pauses inside an advert that would cut it in two, numbered after the highest number kept under the lock a
/// round of learning keeps its candidate under. While learning is on, it takes the place of the version in use when that
/// one was made by hand too, has never been evaluated and was not chosen by hand; otherwise it is kept as a candidate,
/// which turning learning on puts in use on the same conditions. The version in use changing is what has the detections
/// made again in spare time. Going back removes that version and puts the one it took the place of in use again,
/// keeping what viewers did with its buttons.
/// </summary>
public partial class AVersionMadeByHandHearsSilenceBelowThePausesInAdverts : Migration
{
    private const string TakeTurn = "SELECT pg_advisory_xact_lock(5243197610103)";

    private const string Made =
        "kinds = 4 AND evidence IS NULL AND number = (SELECT max(number) FROM segment_model_version WHERE kinds = 4 AND evidence IS NULL)";

    private const string FourKinds =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}, "Opening": {"bias": -6.0, "features": {"pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"pastEndingShare": 4.0}}}}""";

    private const string FourKindsScales =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}}""";

    private const string BelowPauses =
        """{"silenceDbfs": -55, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}""";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql(
            $$"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at)
            SELECT
                coalesce(max(number), 0) + 1,
                'Candidate',
                false,
                4,
                '{{FourKinds}}',
                '{}',
                '{}',
                '{{BelowPauses}}',
                '{{FourKindsScales}}',
                now()
            FROM segment_model_version
            """);
        migrationBuilder.Sql(
            """
            UPDATE segment_model_version
            SET state = 'Retired', retired_at = now()
            WHERE state = 'Active' AND NOT by_hand AND evidence IS NULL
              AND number < (SELECT max(number) FROM segment_model_version)
              AND EXISTS (SELECT 1 FROM segment_settings WHERE learning)
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
        migrationBuilder.Sql($"UPDATE segment_skip_event SET model_version = NULL WHERE model_version IN (SELECT number FROM segment_model_version WHERE {Made})");
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
            WHERE evidence IS NULL AND state = 'Retired'
              AND retired_at = (SELECT adopted_at FROM segment_model_version WHERE {Made} AND retired_at = now())
            """);
        migrationBuilder.Sql($"DELETE FROM segment_model_version WHERE {Made}");
    }
}
