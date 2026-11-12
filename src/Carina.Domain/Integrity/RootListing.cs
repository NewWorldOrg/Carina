using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed class RootListing
{
    private readonly Dictionary<string, StoredFile> byPath;

    private RootListing(OutputRoot root, bool reachable, IReadOnlyList<StoredFile> files, StoragePlace place)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);

        if (!Enum.IsDefined(place))
        {
            throw new ArgumentOutOfRangeException(nameof(place), place, "A listing is of a place the sweep walks.");
        }

        byPath = new Dictionary<string, StoredFile>(StringComparer.Ordinal);

        foreach (StoredFile file in files)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (!byPath.TryAdd(file.Path, file))
            {
                throw new ArgumentException(
                    $"A directory holds one file per path, so '{file.Path}' cannot be listed twice.",
                    nameof(files));
            }
        }

        Root = root;
        Reachable = reachable;
        Files = [.. files];
        Place = place;
    }

    public OutputRoot Root { get; }

    public bool Reachable { get; }

    public IReadOnlyList<StoredFile> Files { get; }

    public StoragePlace Place { get; }

    /// <summary>
    /// Whether the place cannot be told from one whose mount has gone: it could not be read, or holds nothing at all.
    /// Nothing under such a place is removed.
    /// </summary>
    public bool LooksUnmounted => !Reachable || Files.Count is 0;

    public static RootListing Of(
        OutputRoot root,
        IReadOnlyList<StoredFile> files,
        StoragePlace place = StoragePlace.Recordings)
        => new(root, true, files, place);

    public static RootListing OutOfReach(OutputRoot root, StoragePlace place = StoragePlace.Recordings)
        => new(root, false, [], place);

    public StoredFile? At(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return byPath.GetValueOrDefault(path);
    }
}
