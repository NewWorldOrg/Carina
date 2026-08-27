using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record StorageRootPath
{
    public StorageRootPath(OutputRoot root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!path.StartsWith('/'))
        {
            throw new ArgumentException(
                $"An output root is mounted at an absolute path, and '{path}' is not one.",
                nameof(path));
        }

        Root = root;
        Path = path;
    }

    public OutputRoot Root { get; }

    public string Path { get; }
}

public sealed record IntegritySettings
{
    public TimeSpan BeforeFirstSweep { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenSweeps { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan BetweenManualSweeps { get; init; } = TimeSpan.FromMinutes(5);

    public IReadOnlyList<StorageRootPath> OutputRoots { get; init; } = [];

    public bool WalksAnything => OutputRoots.Count > 0;
}
