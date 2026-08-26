using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Integrity;

public sealed class LocalRecordingFileSurvey(
    IntegritySettings settings,
    ILogger<LocalRecordingFileSurvey> logger) : IRecordingFileSurvey
{
    public static readonly EnumerationOptions HowItWalks = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public Task<IReadOnlyList<OutputRoot>> RootsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OutputRoot> roots = [.. settings.OutputRoots.Select(mounted => mounted.Root)];

        return Task.FromResult(roots);
    }

    public Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();

        StorageRootPath? mounted = settings.OutputRoots
            .FirstOrDefault(candidate => candidate.Root.Equals(root));

        if (mounted is null)
        {
            logger.LogWarning(
                "Output root {Root} is named by the ledger but nothing tells this process where it is mounted.",
                root.Value);

            return Task.FromResult(RootListing.OutOfReach(root));
        }

        return Task.FromResult(Walked(root, mounted.Path, cancellationToken));
    }

    private RootListing Walked(OutputRoot root, string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                logger.LogWarning(
                    "Output root {Root} is configured at {Path}, and there is no directory there.",
                    root.Value,
                    path);

                return RootListing.OutOfReach(root);
            }

            List<StoredFile> files = [];

            foreach (string entry in Directory.EnumerateFiles(path, "*", HowItWalks))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var found = new FileInfo(entry);

                if (found.Exists)
                {
                    files.Add(new StoredFile(Under(path, entry), found.Length));
                }
            }

            return RootListing.Of(root, files);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "Output root {Root} at {Path} could not be read, so nothing under it is judged this time.",
                root.Value,
                path);

            return RootListing.OutOfReach(root);
        }
    }

    private static string Under(string root, string entry)
        => Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
}
