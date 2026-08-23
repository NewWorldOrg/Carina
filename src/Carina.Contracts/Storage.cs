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
        name is not null
        && roots is not null
        && roots.Any(root => string.Equals(root.Name, name, StringComparison.Ordinal));
}
