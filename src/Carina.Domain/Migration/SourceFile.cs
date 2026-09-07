namespace Carina.Domain.Migration;

public sealed record SourceFile
{
    public SourceFile(string path, long sizeBytes)
    {
        Path = SourcePath.Of(path, nameof(path));

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "A file is not smaller than empty.");
        }

        SizeBytes = sizeBytes;
    }

    public string Path { get; }

    public long SizeBytes { get; }
}
