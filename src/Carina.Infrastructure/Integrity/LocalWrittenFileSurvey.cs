using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Integrity;

/// <summary>
/// Walks the roots this process encodes into and the directory it draws thumbnails into. A place is
/// left out when it shares a name with a recording root, or when its directory is a recording root's,
/// or one inside it or around it, or one already taken.
/// </summary>
public sealed class LocalWrittenFileSurvey : IWrittenFileSurvey
{
    public static readonly OutputRoot ThumbnailPlace = new("thumbnails");

    private readonly IReadOnlyList<Walkable> places;

    private readonly ILogger<LocalWrittenFileSurvey> logger;

    public LocalWrittenFileSurvey(
        IntegritySettings recordings,
        EncodeSettings encodes,
        ThumbnailSettings thumbnails,
        ILogger<LocalWrittenFileSurvey> logger)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(encodes);
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        List<Walkable> taken = [.. recordings.OutputRoots.Select(root => Walkable.Of(root, StoragePlace.Recordings))];
        List<Walkable> offered = [.. encodes.OutputRoots.Select(root => Walkable.Of(root, StoragePlace.Encodes))];

        if (thumbnails.WrittenTo is { } drawnInto)
        {
            offered.Add(Walkable.Of(new StorageRootPath(ThumbnailPlace, drawnInto), StoragePlace.Thumbnails));
        }

        List<Walkable> kept = [];

        foreach (Walkable place in offered)
        {
            if (taken.FirstOrDefault(other => other.Collides(place)) is { } other)
            {
                logger.LogWarning(
                    "{Place} {Root} at {Path} is not checked against the ledger: it is {Other} {OtherRoot} at "
                    + "{OtherPath} under another name, or inside it, or around it.",
                    place.Place,
                    place.Root.Value,
                    place.Path,
                    other.Place,
                    other.Root.Value,
                    other.Path);

                continue;
            }

            taken.Add(place);
            kept.Add(place);
        }

        places = kept;
    }

    public IReadOnlyList<OutputRoot> Places => [.. places.Select(place => place.Root)];

    public Task<RootListing> ListAsync(OutputRoot place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        cancellationToken.ThrowIfCancellationRequested();

        if (places.FirstOrDefault(candidate => candidate.Root.Equals(place)) is not { } found)
        {
            throw new ArgumentException(
                $"'{place.Value}' is not one of the places this process writes into and walks.",
                nameof(place));
        }

        return Task.FromResult(
            LocalRecordingFileSurvey.Walk(found.Root, found.Path, found.Place, logger, cancellationToken));
    }

    public IReadOnlyList<DeclaredFile> PicturesOf(IReadOnlyList<LedgerFile> ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return places.Any(place => place.Place is StoragePlace.Thumbnails)
            ? [.. ledger.Select(row => new DeclaredFile(ThumbnailPlace, row.Id.Wire + ThumbnailJob.Extension))]
            : [];
    }

    /// <summary>
    /// Where a place is on this machine, when this process walks it or the place it writes into.
    /// </summary>
    public string? WhereIs(OutputRoot place)
    {
        ArgumentNullException.ThrowIfNull(place);

        return places.FirstOrDefault(candidate => candidate.Root.Equals(place))?.Path;
    }

    private sealed record Walkable(OutputRoot Root, string Path, StoragePlace Place)
    {
        public static Walkable Of(StorageRootPath mounted, StoragePlace place)
            => new(mounted.Root, System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(mounted.Path)), place);

        public bool Collides(Walkable other)
            => Root.Equals(other.Root)
               || string.Equals(Path, other.Path, StringComparison.Ordinal)
               || Within(Path, other.Path)
               || Within(other.Path, Path);

        private static bool Within(string outer, string inner)
            => inner.StartsWith(
                outer.EndsWith('/') ? outer : outer + '/',
                StringComparison.Ordinal);
    }
}
