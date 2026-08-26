namespace Carina.Domain.Integrity;

public sealed record StoredFile
{
    public StoredFile(string path, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path.StartsWith('/'))
        {
            throw new ArgumentException(
                "A path is read from the output root down, so it does not start at the top of the disk.",
                nameof(path));
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
