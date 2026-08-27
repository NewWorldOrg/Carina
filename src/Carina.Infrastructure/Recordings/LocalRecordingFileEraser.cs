using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class LocalRecordingFileEraser(
    IntegritySettings mounts,
    ThumbnailSettings pictures,
    ILogger<LocalRecordingFileEraser> logger) : IRecordingFileEraser
{
    public static bool LiesDirectlyUnder(string room, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return string.Equals(Path.GetDirectoryName(full), held, StringComparison.Ordinal);
    }

    public async Task<RecordingErasure> EraseAsync(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        if (Mounted(root) is not { } room)
        {
            return RecordingErasure.Refused(
                ErasureFault.RootOutOfReach,
                $"Output root {root.Value} is named by the ledger and nothing tells this process where it is "
                + "mounted, so no file under it is removed.");
        }

        if (StandsOpen(root, room) is { } shut)
        {
            return shut;
        }

        string recorded = Path.Combine(room, fileName.Value);

        if (!LiesDirectlyUnder(room, recorded))
        {
            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"File {fileName.Value} does not resolve to a file directly under output root {root.Value}, "
                + "so nothing is removed.");
        }

        int removed = 0;
        bool recordedWasThere = File.Exists(recorded);

        if (Unlink(recorded) is { } refused)
        {
            return refused;
        }

        removed += recordedWasThere ? 1 : 0;

        if (pictures.WrittenTo is { } gallery)
        {
            string drawn = Path.Combine(gallery, id.Wire + ThumbnailJob.Extension);
            bool drawnWasThere = File.Exists(drawn);

            if (Unlink(drawn) is { } left)
            {
                return left;
            }

            removed += drawnWasThere ? 1 : 0;
        }

        logger.LogInformation(
            "Recording {Recording} was asked for by hand and its file under output root {Root} is gone.",
            id.Wire,
            root.Value);

        return RecordingErasure.Erased(removed);
    }

    private RecordingErasure? StandsOpen(OutputRoot root, string room)
    {
        try
        {
            if (!Directory.Exists(room))
            {
                return RecordingErasure.Refused(
                    ErasureFault.RootOutOfReach,
                    $"Output root {root.Value} is configured at a path with no directory on it, so a file "
                    + "reported missing under it says nothing about whether it was ever there.");
            }

            using IEnumerator<string> held = Directory
                .EnumerateFiles(room, "*", LocalRecordingFileSurvey.HowItWalks)
                .GetEnumerator();

            if (!held.MoveNext())
            {
                return RecordingErasure.Refused(
                    ErasureFault.RootOutOfReach,
                    $"Output root {root.Value} holds no file at all, which is what it looks like when its "
                    + "mount has gone, so nothing under it is removed.");
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "Output root {Root} could not be read, so nothing under it is removed.",
                root.Value);

            return RecordingErasure.Refused(
                ErasureFault.RootOutOfReach,
                $"Output root {root.Value} could not be read, so nothing under it is removed.");
        }
    }

    private RecordingErasure? Unlink(string path)
    {
        try
        {
            File.Delete(path);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "A file this recording is made of could not be removed.");

            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"A file this recording is made of could not be removed: {failure.Message}");
        }
    }

    private string? Mounted(OutputRoot root)
        => mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root))?.Path;
}
