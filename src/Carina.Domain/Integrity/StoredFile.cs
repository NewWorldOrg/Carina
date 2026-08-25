namespace Carina.Domain.Integrity;

public sealed record StoredFile
{
    public const int MaxPathLength = 1024;

    public StoredFile(string path, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Length > MaxPathLength)
        {
            throw new ArgumentException(
                $"A path under an output root is at most {MaxPathLength} characters, "
                + $"and this one has {path.Length}.",
                nameof(path));
        }

        if (path.StartsWith('/'))
        {
            throw new ArgumentException(
                "A path is read from the output root down, so it does not start at the top of the disk.",
                nameof(path));
        }

        if (path.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("A path stays inside the room it was read from.", nameof(path));
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("A path separates its parts with '/'.", nameof(path));
        }

        if (path.Trim().Length != path.Length)
        {
            throw new ArgumentException("A path carries no surrounding space.", nameof(path));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "A file is not smaller than empty.");
        }

        Path = path;
        SizeBytes = sizeBytes;
    }

    public string Path { get; }

    public long SizeBytes { get; }
}
