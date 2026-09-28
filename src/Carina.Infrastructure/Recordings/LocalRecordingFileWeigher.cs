using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class LocalRecordingFileWeigher(
    IntegritySettings mounts,
    ILogger<LocalRecordingFileWeigher> logger) : IRecordingFileWeigher
{
    public Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken)
        => Task.FromResult(Read(root, fileName, found => found.Length, cancellationToken));

    public Task<DateTime?> LastWrittenAsync(
        OutputRoot root,
        RecordingFileName fileName,
        CancellationToken cancellationToken)
        => Task.FromResult(Read(root, fileName, found => found.LastWriteTimeUtc, cancellationToken));

    private T? Read<T>(
        OutputRoot root,
        RecordingFileName fileName,
        Func<FileInfo, T> reading,
        CancellationToken cancellationToken)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        StorageRootPath? mounted = mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root));

        if (mounted is null)
        {
            logger.LogWarning(
                "Output root {Root} is named by the ledger and nothing tells this process where it is mounted, "
                + "so the file {File} under it cannot be read.",
                root.Value,
                fileName.Value);

            return null;
        }

        try
        {
            FileInfo found = new(Path.Combine(mounted.Path, fileName.Value));

            return found.Exists ? reading(found) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be read off the disk.",
                fileName.Value,
                root.Value);

            return null;
        }
    }
}
