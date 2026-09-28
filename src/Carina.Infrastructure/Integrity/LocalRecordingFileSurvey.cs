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
        => Walk(root, path, StoragePlace.Recordings, logger, cancellationToken);

    /// <summary>
    /// Lists every file under <paramref name="path"/> without following a link, or says the place is out
    /// of reach when it is not there or cannot be read.
    /// </summary>
    public static RootListing Walk(
        OutputRoot root,
        string path,
        StoragePlace place,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!Directory.Exists(path))
            {
                logger.LogWarning(
                    "{Place} {Root} is configured at {Path}, and there is no directory there.",
                    place,
                    root.Value,
                    path);

                return RootListing.OutOfReach(root, place);
            }

            List<StoredFile> files = [];

            foreach (string entry in Directory.EnumerateFiles(path, "*", HowItWalks))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo found = new(entry);

                if (found.Exists)
                {
                    files.Add(new StoredFile(Under(path, entry), found.Length, found.LastWriteTimeUtc));
                }
            }

            return RootListing.Of(root, files, place);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "{Place} {Root} at {Path} could not be read, so nothing under it is judged this time.",
                place,
                root.Value,
                path);

            return RootListing.OutOfReach(root, place);
        }
    }

    private static string Under(string root, string entry)
        => Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
}
