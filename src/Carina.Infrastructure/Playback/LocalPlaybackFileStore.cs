using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Playback;

public sealed class LocalPlaybackFileStore(
    IntegritySettings mounts,
    ILogger<LocalPlaybackFileStore> logger) : IPlaybackFileStore
{
    public PlaybackFileSearch Find(OutputRoot root, RecordingFileName fileName)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);

        if (Where(root, fileName) is not { } path)
        {
            return PlaybackFileSearch.Missing(PlaybackFileAbsence.OutOfReach);
        }

        try
        {
            var found = new FileInfo(path);

            if (found.Exists)
            {
                return PlaybackFileSearch.Of(new PlaybackFile(root, fileName, found.Length));
            }

            return PlaybackFileSearch.Missing(
                Directory.Exists(Path.GetDirectoryName(path))
                    ? PlaybackFileAbsence.Gone
                    : PlaybackFileAbsence.OutOfReach);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be looked at.",
                fileName.Value,
                root.Value);

            return PlaybackFileSearch.Missing(PlaybackFileAbsence.OutOfReach);
        }
    }

    public PlaybackFileOpening OpenRead(PlaybackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (Where(file.Root, file.Name) is not { } path)
        {
            return PlaybackFileOpening.Missing(PlaybackFileAbsence.OutOfReach);
        }

        try
        {
            return PlaybackFileOpening.Of(File.OpenRead(path));
        }
        catch (FileNotFoundException)
        {
            return PlaybackFileOpening.Missing(PlaybackFileAbsence.Gone);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be opened to be read.",
                file.Name.Value,
                file.Root.Value);

            return PlaybackFileOpening.Missing(PlaybackFileAbsence.OutOfReach);
        }
    }

    public StreamSource? SourceOf(PlaybackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Where(file.Root, file.Name) is { } path ? new StreamSource(path) : null;
    }

    private string? Where(OutputRoot root, RecordingFileName fileName)
    {
        StorageRootPath? mounted = mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root));

        if (mounted is not null)
        {
            return Path.Combine(mounted.Path, fileName.Value);
        }

        logger.LogWarning(
            "Output root {Root} is named by the ledger and nothing tells this process where it is mounted, "
            + "so the file {File} under it cannot be played.",
            root.Value,
            fileName.Value);

        return null;
    }
}
