using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class LocalRecordingFileWeigher(
    IntegritySettings mounts,
    ILogger<LocalRecordingFileWeigher> logger) : IRecordingFileWeigher
{
    public Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        StorageRootPath? mounted = mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root));

        if (mounted is null)
        {
            logger.LogWarning(
                "Output root {Root} is named by the ledger and nothing tells this process where it is mounted, "
                + "so the file {File} under it cannot be weighed.",
                root.Value,
                fileName.Value);

            return Task.FromResult<long?>(null);
        }

        try
        {
            var found = new FileInfo(Path.Combine(mounted.Path, fileName.Value));

            return Task.FromResult(found.Exists ? found.Length : (long?)null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The file {File} under output root {Root} could not be read off the disk.",
                fileName.Value,
                root.Value);

            return Task.FromResult<long?>(null);
        }
    }
}
