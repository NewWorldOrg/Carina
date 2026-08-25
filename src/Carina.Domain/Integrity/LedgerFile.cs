using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record LedgerFile
{
    private LedgerFile(RecordingId id, OutputRoot root, RecordingFileName fileName, long? sizeObserved)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);

        if (sizeObserved is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeObserved),
                sizeObserved,
                "A file is not smaller than empty.");
        }

        Id = id;
        Root = root;
        FileName = fileName;
        SizeObserved = sizeObserved;
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public long? SizeObserved { get; }

    public static LedgerFile StillWriting(RecordingId id, OutputRoot root, RecordingFileName fileName)
        => new(id, root, fileName, null);

    public static LedgerFile Ended(RecordingId id, OutputRoot root, RecordingFileName fileName, long sizeObserved)
        => new(id, root, fileName, sizeObserved);
}
