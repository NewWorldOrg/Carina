using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed class RootListing
{
    private readonly Dictionary<string, StoredFile> byName;

    private RootListing(OutputRoot root, bool reachable, IReadOnlyList<StoredFile> files)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);

        byName = new Dictionary<string, StoredFile>(StringComparer.Ordinal);

        foreach (StoredFile file in files)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (!byName.TryAdd(file.Name, file))
            {
                throw new ArgumentException(
                    $"A directory holds one file per name, so '{file.Name}' cannot be listed twice.",
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

    public StoredFile? Named(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return byName.GetValueOrDefault(fileName);
    }
}
