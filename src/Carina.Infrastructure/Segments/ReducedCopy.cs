using System.Globalization;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// A reduced copy of one recording, made apart from this application in the shape <see cref="Shape"/>
/// names, one directory a copy: the sound as the sum of its two channels (<see cref="Sum"/>) and as their
/// difference (<see cref="Difference"/>), the picture shrunk to the size of a frame's light at every frame
/// (<see cref="Frames"/>), the four corners laid out as <see cref="CornerTiles"/> four times a second
/// (<see cref="Corners"/>), the captions taken from the recording when there were any
/// (<see cref="CaptionsFile"/>), and beside them its manifest, what ffprobe read of the recording's file
/// (<see cref="Probe"/>), and the rows of the recording, its reservation and its programme as they stood.
/// The copy's time zero is where the programme's first picture or first sound began, whichever came first,
/// and lies <see cref="StartsAt"/> into the recording's own time; the captions keep the recording's clock.
/// </summary>
public sealed record ReducedCopy(string Directory, RecordingId Id, ProgrammeCopy Programme, TimeSpan StartsAt, string? Captions)
{
    public const string Shape = "anime-material-v1";

    public const string Manifest = "manifest.json";

    public const string Rows = "programme.json";

    public const string Probe = "source-probe.json";

    public const string Sum = "audio.opus";

    public const string Difference = "side.opus";

    public const string Frames = "frames.mp4";

    public const string Corners = "corners.mp4";

    public const string CaptionsFile = "captions.carinacc";

    public string PathOf(string file) => Path.Combine(Directory, file);
}

/// <summary>
/// A reduced copy read from its directory, or why it is not imported.
/// </summary>
public sealed record ReducedCopyRead(ReducedCopy? Copy, string? Refusal);

/// <summary>
/// Reads a reduced copy from its directory: every file it is read from is there and holds something, its
/// manifest names the shape <see cref="ReducedCopy.Shape"/> and the same recording as the rows copied beside
/// it, and those rows give the copy of the programme. The programme ends when the guide said, or else when
/// its reservation said once that end had been announced. Where the copy starts on the recording's own time
/// is where the programme's first picture or first sound began, whichever came first, after the file's
/// clock began, both as the probe of the file read them. Nothing in the directory is written.
/// </summary>
public static class ReducedCopyReader
{
    public const long LargestDescription = 1 << 20;

    private static readonly string[] Required =
    [
        ReducedCopy.Manifest,
        ReducedCopy.Rows,
        ReducedCopy.Probe,
        ReducedCopy.Sum,
        ReducedCopy.Difference,
        ReducedCopy.Frames,
        ReducedCopy.Corners,
    ];

    private static readonly JsonSerializerOptions Columns = new(JsonSerializerDefaults.Web);

    public static ReducedCopyRead Read(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (Required.FirstOrDefault(file => !Holds(directory, file)) is { } missing)
        {
            return new ReducedCopyRead(null, $"it holds no {missing}, or the file is empty");
        }

        try
        {
            using JsonDocument manifest = Described(directory, ReducedCopy.Manifest);
            using JsonDocument rows = Described(directory, ReducedCopy.Rows);
            using JsonDocument probe = Described(directory, ReducedCopy.Probe);

            return Read(directory, manifest.RootElement, rows.RootElement, probe.RootElement);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException or FormatException
                                               or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return new ReducedCopyRead(null, unreadable.Message);
        }
    }

    private static ReducedCopyRead Read(string directory, JsonElement manifest, JsonElement rows, JsonElement probe)
    {
        string? shape = Field(manifest, "format") is { ValueKind: JsonValueKind.String } format ? format.GetString() : null;

        if (!string.Equals(shape, ReducedCopy.Shape, StringComparison.Ordinal))
        {
            return new ReducedCopyRead(null, $"its {ReducedCopy.Manifest} names the shape '{shape}', not {ReducedCopy.Shape}");
        }

        JsonElement recording = Row(rows, "recording")
            ?? throw new InvalidDataException($"its {ReducedCopy.Rows} holds no row of the recording");
        RecordingId id = Guid.TryParse(Text(recording, "id"), CultureInfo.InvariantCulture, out Guid named) && named != Guid.Empty
            ? new RecordingId(named)
            : throw new InvalidDataException($"its {ReducedCopy.Rows} gives the recording no id that can be read");

        if (!Guid.TryParse(Field(manifest, "recording_id")?.GetString(), CultureInfo.InvariantCulture, out Guid manifestNames)
            || manifestNames != id.Value)
        {
            return new ReducedCopyRead(null, $"its {ReducedCopy.Manifest} and its {ReducedCopy.Rows} name different recordings");
        }

        string captions = Path.Combine(directory, ReducedCopy.CaptionsFile);
        ProgrammeCopy programme = Copied(recording, Row(rows, "programme"), Row(rows, "reservation"));

        return new ReducedCopyRead(
            new ReducedCopy(directory, id, programme, StartsAt(probe, programme.ServiceId), File.Exists(captions) ? captions : null),
            null);
    }

    private static TimeSpan StartsAt(JsonElement probe, ServiceId service)
    {
        decimal fileBegins = Seconds(Row(probe, "format"), "start_time")
            ?? throw new InvalidDataException($"its {ReducedCopy.Probe} gives no start of the file");
        JsonElement programme = Programmes(probe).FirstOrDefault(listed => Field(listed, "program_id")?.GetInt32() == service.Value) is { ValueKind: JsonValueKind.Object } found
            ? found
            : throw new InvalidDataException($"its {ReducedCopy.Probe} describes no programme {service.Value}");
        decimal copyBegins = new[] { First(programme, "video"), First(programme, "audio") }.Min()
            ?? throw new InvalidDataException($"its {ReducedCopy.Probe} gives no start of the programme's picture or sound");

        return copyBegins >= fileBegins
            ? TimeSpan.FromTicks((long)((copyBegins - fileBegins) * TimeSpan.TicksPerSecond))
            : throw new InvalidDataException($"its {ReducedCopy.Probe} has the programme begin before the file");
    }

    private static IEnumerable<JsonElement> Programmes(JsonElement probe)
        => Field(probe, "programs") is { ValueKind: JsonValueKind.Array } listed ? listed.EnumerateArray() : [];

    private static decimal? First(JsonElement programme, string kind)
        => Field(programme, "streams") is { ValueKind: JsonValueKind.Array } streams
            && streams.EnumerateArray().FirstOrDefault(stream => Field(stream, "codec_type")?.GetString() == kind) is { ValueKind: JsonValueKind.Object } first
            ? Seconds(first, "start_time")
            : null;

    private static decimal? Seconds(JsonElement? row, string name)
        => row is { } held && Field(held, name)?.GetString() is { } written
            ? decimal.Parse(written, NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;

    private static ProgrammeCopy Copied(JsonElement recording, JsonElement? programme, JsonElement? reservation)
    {
        DateTime starts = Instant(recording, "programme_start_at");
        string name = Text(recording, "snapshot_name");
        string audio = Text(recording, "snapshot_audio");

        return new ProgrammeCopy(
            new NetworkId(Field(recording, "network_id")?.GetInt32() ?? throw Missing("network_id")),
            new ServiceId(Field(recording, "service_id")?.GetInt32() ?? throw Missing("service_id")),
            starts,
            EndOf(starts, programme, reservation),
            Instant(recording, "started_at_actual"),
            name,
            Field(recording, "snapshot_genres")?.Deserialize<List<ProgrammeGenre>>(Columns) ?? throw Missing("snapshot_genres"),
            ProgrammeMarks.In(name, Text(recording, "snapshot_summary")),
            null,
            Enum.TryParse(audio, ignoreCase: false, out AudioMode mode) && Enum.IsDefined(mode)
                ? mode
                : throw new InvalidDataException($"the recording's sound '{audio}' is none of the modes kept"),
            null);
    }

    private static DateTime? EndOf(DateTime starts, JsonElement? programme, JsonElement? reservation)
    {
        DateTime? guided = programme is { } announced ? OptionalInstant(announced, "end_at") : null;
        DateTime? reserved = reservation is { } booked && Field(booked, "end_at_confirmed")?.GetBoolean() is true
            ? OptionalInstant(booked, "end_at")
            : null;

        if (guided > starts)
        {
            return guided;
        }

        return reserved > starts ? reserved : null;
    }

    private static bool Holds(string directory, string file)
    {
        FileInfo found = new(Path.Combine(directory, file));

        return found.Exists && found.Length > 0;
    }

    private static JsonDocument Described(string directory, string file)
    {
        string path = Path.Combine(directory, file);

        if (new FileInfo(path).Length > LargestDescription)
        {
            throw new InvalidDataException($"its {file} is larger than {LargestDescription} bytes");
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException unreadable)
        {
            throw new InvalidDataException($"its {file} is not JSON that can be read: {unreadable.Message}", unreadable);
        }
    }

    private static JsonElement? Row(JsonElement rows, string name)
        => rows.ValueKind is JsonValueKind.Object && Field(rows, name) is { ValueKind: JsonValueKind.Object } row ? row : null;

    private static JsonElement? Field(JsonElement row, string name)
        => row.ValueKind is JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value) && value.ValueKind is not JsonValueKind.Null
            ? value
            : null;

    private static string Text(JsonElement row, string name) => Field(row, name)?.GetString() ?? throw Missing(name);

    private static DateTime Instant(JsonElement row, string name) => OptionalInstant(row, name) ?? throw Missing(name);

    private static DateTime? OptionalInstant(JsonElement row, string name) => Field(row, name)?.GetDateTimeOffset().UtcDateTime;

    private static InvalidDataException Missing(string name) => new($"its {ReducedCopy.Rows} gives no {name}");
}
