using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Recording;

public enum ErasureRefusal
{
    None = 0,

    NotARecording = 1,

    UnknownOutputRoot = 2,

    BeingWritten = 3,

    RootOutOfReach = 4,

    FileLeftBehind = 5,
}

public sealed record FileErasure
{
    private FileErasure(ErasureRefusal refusal, string detail, bool fileRemoved)
    {
        Refusal = refusal;
        Detail = detail;
        FileRemoved = fileRemoved;
    }

    public ErasureRefusal Refusal { get; }

    public string Detail { get; }

    public bool FileRemoved { get; }

    public static FileErasure Erased(bool fileRemoved) =>
        new(ErasureRefusal.None, string.Empty, fileRemoved);

    public static FileErasure Refused(ErasureRefusal refusal, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        if (refusal is ErasureRefusal.None || !Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A refusal names the one reason it refused."
            );
        }

        return new FileErasure(refusal, detail, fileRemoved: false);
    }
}

public sealed class RecordingEraser(
    DriverConfiguration configuration,
    TunerSessionManager sessions,
    ILogger<RecordingEraser> logger
)
{
    public static bool NamesARecordingFile(string? recordingId) =>
        WireName.IsUsable(recordingId) && !recordingId!.StartsWith('.');

    public static bool LiesDirectlyUnder(string room, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return string.Equals(Path.GetDirectoryName(full), held, StringComparison.Ordinal);
    }

    public FileErasure Erase(string? recordingId, string? outputRoot)
    {
        if (!NamesARecordingFile(recordingId))
        {
            return FileErasure.Refused(
                ErasureRefusal.NotARecording,
                $"A recording is named by {WireName.Description}, and never by a name beginning with '.', "
                    + "so this driver holds no file of that name to remove."
            );
        }

        if (!configuration.TryResolveOutputRoot(outputRoot, out string? room))
        {
            string declared = string.Join(
                ", ",
                (configuration.OutputRoots ?? []).Select(root => root?.Name)
            );

            return FileErasure.Refused(
                ErasureRefusal.UnknownOutputRoot,
                $"This driver declares no output root called '{outputRoot}'; it declares {declared}."
            );
        }

        using IDisposable? held = sessions.ClaimForErasure(recordingId!);

        if (held is null)
        {
            return FileErasure.Refused(
                ErasureRefusal.BeingWritten,
                $"The recording '{recordingId}' is being written, so its file stays where it is until "
                    + "the session writing it has ended."
            );
        }

        if (LostMount(room, outputRoot!) is { } gone)
        {
            return gone;
        }

        string recorded = Path.Combine(room, RecordingFile.Of(recordingId));

        if (!LiesDirectlyUnder(room, recorded))
        {
            return FileErasure.Refused(
                ErasureRefusal.NotARecording,
                $"The file recording '{recordingId}' is named by does not sit directly in the output root "
                    + $"'{outputRoot}', so nothing is removed."
            );
        }

        bool wasThere = File.Exists(recorded);

        try
        {
            File.Delete(recorded);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file of recording {RecordingId} under output root {Root} could not be removed.",
                recordingId,
                outputRoot
            );

            return FileErasure.Refused(
                ErasureRefusal.FileLeftBehind,
                $"The file of recording '{recordingId}' could not be removed: {failure.Message}"
            );
        }

        logger.LogInformation(
            "The file of recording {RecordingId} under output root {Root} was removed on request; it {WasThere}.",
            recordingId,
            outputRoot,
            wasThere ? "was there" : "was already gone"
        );

        return FileErasure.Erased(wasThere);
    }

    private FileErasure? LostMount(string room, string outputRoot)
    {
        try
        {
            using IEnumerator<string> walking = Directory
                .EnumerateFiles(room, "*", SearchOption.AllDirectories)
                .GetEnumerator();

            if (!walking.MoveNext())
            {
                return FileErasure.Refused(
                    ErasureRefusal.RootOutOfReach,
                    $"Output root '{outputRoot}' holds no file at all, which is what it looks like when its "
                        + "mount has gone, so nothing under it is removed."
                );
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "Output root {Root} could not be read, so nothing under it is removed.",
                outputRoot
            );

            return FileErasure.Refused(
                ErasureRefusal.RootOutOfReach,
                $"Output root '{outputRoot}' could not be read, so a file reported missing under it says "
                    + "nothing about whether it was ever there, and nothing under it is removed."
            );
        }
    }
}
