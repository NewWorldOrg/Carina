namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

/// <summary>
/// Statements that lay the rows that remember a version of the model by its number: a detection, the chapters of an
/// encode and a round of learning, each written as the store keeps it.
/// </summary>
internal static class RowsNamingAVersion
{
    public const string Profile = "4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b";

    public const string Destination = "5e6f7081-92a3-4b4c-85d6-e7f8091a2b3c";

    /// <summary>The tables these rows and the versions are kept in, emptied as a database just migrated has them.</summary>
    public const string Emptied = "TRUNCATE segment_model_version, segment_detection, segment_skip_event, segment_learning_round, encode_job, encode_destination, encode_profile CASCADE";

    public static string Detected(int version)
        => $"INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), {version}, 'NothingFound', '[{{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}}]', 0, '[]', 2, now(), now())";

    public static string Encoded(int version)
    {
        Guid recording = Guid.NewGuid();

        return $"""
                INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
                VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, timestamptz '2026-10-01 00:00:00+00');
                INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
                VALUES ('{Destination}', 'Shelf', 'encodes', '{Profile}', timestamptz '2026-10-01 00:00:00+00');
                INSERT INTO encode_job (
                    id, recording_id, profile_id, destination_id, output_root, status, attempt,
                    queued_at, started_at, ended_at, artefact_name,
                    chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at, chapters_model_version)
                VALUES (
                    gen_random_uuid(), '{recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                    timestamptz '2026-10-01 03:00:00+00', timestamptz '2026-10-01 03:00:05+00', timestamptz '2026-10-01 03:30:00+00',
                    '{recording:N}.{Guid.Parse(Profile):N}.mp4', 'Segments', 'Marked', 0.2, timestamptz '2026-10-01 05:00:00+00', {version});
                """;
    }

    public static string Round(string day, int candidate, int inUseVersion)
        => $"""
            INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, verdict, in_use_metrics, in_use_version)
            VALUES ('{day}', '{day}T00:00:00Z', {candidate}, 'Kept', 'NoBetterAtBoundaries', NULL, {inUseVersion})
            """;
}
