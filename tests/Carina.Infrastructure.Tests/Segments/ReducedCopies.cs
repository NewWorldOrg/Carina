using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Segments;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// What a synthetic reduced copy describes of itself: the shape its manifest names, the recording it is a
/// copy of, where the recording's file, its programme's first picture and first sound and another stream
/// began as the probe of the file read them, and the rows of that recording, its programme in the guide and
/// its reservation as they are copied beside it.
/// </summary>
internal sealed record CopyDescription
{
    public static readonly DateTime Starts = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);

    public RecordingId Id { get; init; } = RecordingId.New();

    public string? Shape { get; init; } = ReducedCopy.Shape;

    public Guid? ManifestNames { get; init; }

    public int Network { get; init; } = 40001;

    public int Service { get; init; } = 4321;

    public DateTime ProgrammeStartsAt { get; init; } = Starts;

    public DateTime StartedAt { get; init; } = Starts.AddSeconds(-5);

    public string Name { get; init; } = "A programme";

    public string Summary { get; init; } = "What it is about";

    public string Audio { get; init; } = "Stereo";

    public DateTime? GuideEndsAt { get; init; } = Starts.AddMinutes(30);

    public DateTime? ReservationEndsAt { get; init; }

    public bool ReservationEndAnnounced { get; init; }

    public bool Reserved { get; init; } = true;

    public string FileBegins { get; init; } = "1000.500000";

    public string? PictureBegins { get; init; } = "1000.900000";

    public string? SoundBegins { get; init; } = "1000.700000";

    public int? Probed { get; init; }

    public JsonObject Probe()
        => new()
        {
            ["programs"] = new JsonArray(
                new JsonObject
                {
                    ["program_id"] = Probed ?? Service,
                    ["start_time"] = FileBegins,
                    ["streams"] = new JsonArray(
                        new JsonObject { ["index"] = 0, ["codec_type"] = "subtitle", ["start_time"] = FileBegins },
                        new JsonObject { ["index"] = 1, ["codec_type"] = "video", ["start_time"] = PictureBegins },
                        new JsonObject { ["index"] = 2, ["codec_type"] = "audio", ["start_time"] = SoundBegins },
                        new JsonObject { ["index"] = 3, ["codec_type"] = "audio", ["start_time"] = FileBegins }),
                },
                new JsonObject
                {
                    ["program_id"] = (Probed ?? Service) + 1,
                    ["streams"] = new JsonArray(new JsonObject { ["index"] = 4, ["codec_type"] = "video", ["start_time"] = FileBegins }),
                }),
            ["format"] = new JsonObject { ["start_time"] = FileBegins },
        };

    public JsonObject Manifest()
        => new()
        {
            ["format"] = Shape,
            ["recording_id"] = (ManifestNames ?? Id.Value).ToString(),
            ["source"] = new JsonObject { ["start_time"] = 1000.5, ["duration_s"] = 1800.0 },
        };

    public JsonObject Rows()
        => new()
        {
            ["from"] = "dev",
            ["recording"] = new JsonObject
            {
                ["id"] = Id.Value.ToString(),
                ["network_id"] = Network,
                ["service_id"] = Service,
                ["event_id"] = 77,
                ["programme_start_at"] = Instant(ProgrammeStartsAt),
                ["started_at_actual"] = Instant(StartedAt),
                ["stopped_at_actual"] = Instant(StartedAt.AddMinutes(31)),
                ["snapshot_name"] = Name,
                ["snapshot_summary"] = Summary,
                ["snapshot_genres"] = new JsonArray(new JsonObject { ["kind"] = 7, ["sort"] = 0 }, new JsonObject { ["kind"] = 7, ["sort"] = 1 }),
                ["snapshot_audio"] = Audio,
                ["file_name"] = $"{Id.Wire}.m2ts",
            },
            ["reservation"] = Reserved
                ? new JsonObject
                {
                    ["end_at"] = ReservationEndsAt is { } reserved ? Instant(reserved) : Instant(Starts.AddMinutes(30)),
                    ["end_at_confirmed"] = ReservationEndAnnounced,
                }
                : null,
            ["programme"] = new JsonObject
            {
                ["start_at"] = Instant(ProgrammeStartsAt),
                ["end_at"] = GuideEndsAt is { } guided ? Instant(guided) : null,
            },
            ["service"] = new JsonObject { ["name"] = "A station" },
        };

    private static string Instant(DateTime utc)
        => new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}

/// <summary>
/// Writes synthetic reduced copies, one directory each, under a shelf.
/// </summary>
internal static class ReducedCopies
{
    public static readonly string[] Media = [ReducedCopy.Sum, ReducedCopy.Difference, ReducedCopy.Frames, ReducedCopy.Corners];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// A copy whose media are a few bytes standing in for what a decoder would read.
    /// </summary>
    public static string Write(string shelf, string name, CopyDescription description, params string[] leftOut)
    {
        string directory = Describe(shelf, name, description, leftOut);

        foreach (string medium in Media.Except(leftOut, StringComparer.Ordinal))
        {
            File.WriteAllBytes(Path.Combine(directory, medium), [1, 2, 3]);
        }

        return directory;
    }

    public static string Describe(string shelf, string name, CopyDescription description, params string[] leftOut)
    {
        string directory = Path.Combine(shelf, name);

        Directory.CreateDirectory(directory);

        if (!leftOut.Contains(ReducedCopy.Manifest, StringComparer.Ordinal))
        {
            File.WriteAllText(Path.Combine(directory, ReducedCopy.Manifest), description.Manifest().ToJsonString(Indented));
        }

        if (!leftOut.Contains(ReducedCopy.Rows, StringComparer.Ordinal))
        {
            File.WriteAllText(Path.Combine(directory, ReducedCopy.Rows), description.Rows().ToJsonString(Indented));
        }

        if (!leftOut.Contains(ReducedCopy.Probe, StringComparer.Ordinal))
        {
            File.WriteAllText(Path.Combine(directory, ReducedCopy.Probe), description.Probe().ToJsonString(Indented));
        }

        return directory;
    }
}
