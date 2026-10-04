using System.Collections.Concurrent;
using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Playback;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Playback;

/// <summary>
/// Reads the codec of an artefact's first picture track by asking ffprobe, and keeps what it read for as
/// long as the file has the same size and the same time it was last written. A file that could not be read
/// is not asked about again for <see cref="UnreadKeptFor"/>.
/// </summary>
public sealed class FfprobeArtefactCodecs(
    IPlaybackFileStore files,
    MachineSettings settings,
    TimeProvider clock) : IArtefactCodecReader
{
    public const string Key = "codec_name";

    public static readonly TimeSpan UnreadKeptFor = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, EncodeCodec> Named = new Dictionary<string, EncodeCodec>(StringComparer.Ordinal)
    {
        ["h264"] = EncodeCodec.H264,
        ["hevc"] = EncodeCodec.H265,
    };

    private readonly ConcurrentDictionary<string, Remembered> remembered = new(StringComparer.Ordinal);

    public static IReadOnlyList<string> Arguments(string artefact)
    {
        ArgumentException.ThrowIfNullOrEmpty(artefact);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-select_streams",
            "v:0",
            "-of",
            FfprobeLengthInvocation.Format,
            "-show_entries",
            $"stream={Key}",
            "-i",
            artefact,
        ];
    }

    public async Task<ArtefactCodecReading> ReadAsync(PlaybackFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (files.SourceOf(file) is not { } source)
        {
            return ArtefactCodecReading.Unread("nothing tells this process where the artefact is mounted");
        }

        string path = source.Value;

        if (Stamped(path) is not { } stamp)
        {
            return ArtefactCodecReading.Unread("the artefact could not be looked at");
        }

        DateTimeOffset now = clock.GetUtcNow();

        if (remembered.TryGetValue(path, out Remembered? kept) && kept.Stamp == stamp && kept.HoldsAt(now))
        {
            return kept.Reading;
        }

        ArtefactCodecReading reading = await ProbeAsync(path, cancellationToken);
        remembered[path] = new Remembered(stamp, reading, reading.Read ? null : now + UnreadKeptFor);

        return reading;
    }

    private async Task<ArtefactCodecReading> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            settings.Prober,
            Arguments(path),
            settings.LongestRead,
            clock,
            cancellationToken);

        if (said.Fault is { } fault)
        {
            return ArtefactCodecReading.Unread($"{fault}: {said.Complained}");
        }

        if (said.ExitCode is not 0)
        {
            return ArtefactCodecReading.Unread(string.Create(
                CultureInfo.InvariantCulture,
                $"the programme exited {said.ExitCode}: {said.Complained}"));
        }

        string? codec = FfprobeRecords.From(said.Said)
            .Select(record => record.Value(Key))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (codec is null)
        {
            return ArtefactCodecReading.Unread($"the programme exited 0 and named no '{Key}' of a picture track");
        }

        return Named.TryGetValue(codec, out EncodeCodec known)
            ? ArtefactCodecReading.Of(known)
            : ArtefactCodecReading.Neither(codec);
    }

    private static FileStamp? Stamped(string path)
    {
        try
        {
            FileInfo found = new(path);

            return found.Exists ? new FileStamp(found.Length, found.LastWriteTimeUtc) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct FileStamp(long Bytes, DateTime WrittenAt);

    private sealed record Remembered(FileStamp Stamp, ArtefactCodecReading Reading, DateTimeOffset? Until)
    {
        public bool HoldsAt(DateTimeOffset now) => Until is not { } until || now < until;
    }
}
