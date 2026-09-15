using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Recording;

public sealed class StrayFileEraser(
    DriverConfiguration configuration,
    TunerSessionManager sessions,
    ILogger<StrayFileEraser> logger
)
{
    public static string? PlaceUnder(string room, string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);

        if (
            string.IsNullOrEmpty(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains('\0', StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment is "" or "." or "..")
        )
        {
            return null;
        }

        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string full = Path.GetFullPath(Path.Combine(held, path));

        return full.StartsWith(held + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : null;
    }

    public static string? RecordingNamedBy(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (
            path.Contains('/', StringComparison.Ordinal)
            || !path.EndsWith(RecordingFile.Extension, StringComparison.Ordinal)
        )
        {
            return null;
        }

        string stem = path[..^RecordingFile.Extension.Length];

        return RecordingEraser.NamesARecordingFile(stem) ? stem : null;
    }

    public FileErasure Erase(StrayFileErasureRequest? request)
    {
        if (request is null)
        {
            return FileErasure.Refused(
                ErasureRefusal.NotUnderTheRoot,
                "The request names no file, so nothing is removed."
            );
        }

        if (!configuration.TryResolveOutputRoot(request.OutputRoot, out string? room))
        {
            string declared = string.Join(
                ", ",
                (configuration.OutputRoots ?? []).Select(root => root?.Name)
            );

            return FileErasure.Refused(
                ErasureRefusal.UnknownOutputRoot,
                $"This driver declares no output root called '{request.OutputRoot}'; it declares {declared}."
            );
        }

        if (PlaceUnder(room, request.Path) is not { } full || ThroughALink(room, full))
        {
            return FileErasure.Refused(
                ErasureRefusal.NotUnderTheRoot,
                $"'{request.Path}' does not name a file inside output root '{request.OutputRoot}' reached "
                    + "without following a link, so nothing is removed."
            );
        }

        string? recordingId = RecordingNamedBy(request.Path);
        using IDisposable? claim = recordingId is null
            ? null
            : sessions.ClaimForErasure(recordingId);

        if (recordingId is not null && claim is null)
        {
            return FileErasure.Refused(
                ErasureRefusal.BeingWritten,
                $"'{request.Path}' is the file of recording '{recordingId}', which is being written, so it "
                    + "stays where it is."
            );
        }

        if (OutputRoom.Missing(room, request.OutputRoot, logger) is { } gone)
        {
            return gone;
        }

        if (Changed(new FileInfo(full), request) is { } changed)
        {
            return FileErasure.Refused(ErasureRefusal.FileChanged, changed);
        }

        try
        {
            File.Delete(full);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "{Path} under output root {Root} could not be removed.",
                request.Path,
                request.OutputRoot
            );

            return FileErasure.Refused(
                ErasureRefusal.FileLeftBehind,
                $"'{request.Path}' under output root '{request.OutputRoot}' could not be removed; the driver "
                    + "log says why."
            );
        }

        logger.LogInformation(
            "{Path} under output root {Root}, which no recording owns, was removed on request.",
            request.Path,
            request.OutputRoot
        );

        return FileErasure.Erased(true);
    }

    private static string? Changed(FileInfo file, StrayFileErasureRequest request)
    {
        if (!file.Exists)
        {
            return $"'{request.Path}' is no longer there, so there is nothing of it to remove.";
        }

        if (file.Length != request.SizeBytes)
        {
            return $"'{request.Path}' holds {file.Length} bytes where {request.SizeBytes} were asked about, "
                + "so it is not the file that was found and it is left where it is.";
        }

        return StrayFileStamp.Truncated(file.LastWriteTimeUtc) == request.LastWrittenAt.UtcDateTime
            ? null
            : $"'{request.Path}' was written to after it was found, so it is not the file that was found "
                + "and it is left where it is.";
    }

    private static bool ThroughALink(string room, string full)
    {
        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string? current = full;

        try
        {
            while (current is not null && !string.Equals(current, held, StringComparison.Ordinal))
            {
                if (Path.Exists(current) && new FileInfo(current).LinkTarget is not null)
                {
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }

            return current is null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
