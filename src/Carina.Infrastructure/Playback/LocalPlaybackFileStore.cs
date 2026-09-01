using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Playback;

public sealed class LocalPlaybackFileStore(
    IntegritySettings mounts,
    ILogger<LocalPlaybackFileStore> logger) : IPlaybackFileStore
{
    public PlaybackFile? Find(OutputRoot root, RecordingFileName fileName)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);

        if (Where(root, fileName) is not { } path)
        {
            return null;
        }

        try
        {
            var found = new FileInfo(path);

            return found.Exists ? new PlaybackFile(root, fileName, found.Length) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be looked at.",
                fileName.Value,
                root.Value);

            return null;
        }
    }

    public Stream? OpenRead(PlaybackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (Where(file.Root, file.Name) is not { } path)
        {
            return null;
        }

        try
        {
            return File.OpenRead(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be opened to be read.",
                file.Name.Value,
                file.Root.Value);

            return null;
        }
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
