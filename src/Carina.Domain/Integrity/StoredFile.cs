namespace Carina.Domain.Integrity;

public sealed record StoredFile
{
    public StoredFile(string name, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "A file is not smaller than empty.");
        }

        Name = name;
        SizeBytes = sizeBytes;
    }

    public string Name { get; }

    public long SizeBytes { get; }
}
