namespace Carina.Contracts;

public sealed record StorageRootDto
{
    public string Name { get; init; } = string.Empty;

    public long FreeBytes { get; init; }

    public long TotalBytes { get; init; }

    public bool Writable { get; init; }
}

public static class StorageRoots
{
    public static bool Declares(IReadOnlyList<StorageRootDto>? roots, string? name) =>
        name is not null && Find(roots, name) is not null;

    public static StorageRootDto? Find(IReadOnlyList<StorageRootDto>? roots, string name)
    {
        if (roots is null)
        {
            return null;
        }

        foreach (StorageRootDto root in roots)
        {
            if (string.Equals(root.Name, name, StringComparison.Ordinal))
            {
                return root;
            }
        }

        return null;
    }
}
