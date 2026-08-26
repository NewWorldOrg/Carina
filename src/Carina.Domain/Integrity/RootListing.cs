using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed class RootListing
{
    private readonly Dictionary<string, StoredFile> byPath;

    private RootListing(OutputRoot root, bool reachable, IReadOnlyList<StoredFile> files)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);

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
    }

    public OutputRoot Root { get; }

    public bool Reachable { get; }

    public IReadOnlyList<StoredFile> Files { get; }

    public static RootListing Of(OutputRoot root, IReadOnlyList<StoredFile> files) => new(root, true, files);

    public static RootListing OutOfReach(OutputRoot root) => new(root, false, []);

    public StoredFile? At(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return byPath.GetValueOrDefault(path);
    }
}
