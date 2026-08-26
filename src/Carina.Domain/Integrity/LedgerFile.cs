using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record LedgerFile
{
    private LedgerFile(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        LedgerClaim? claim,
        long? sizeObserved)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);

        if (claim is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(claim), claim, "A ledger claim is one the sweep can read.");
        }

        if (claim is null != sizeObserved is null)
        {
            throw new ArgumentException(
                "A recording the ledger has weighed says what it found, and one it has not says nothing.",
                nameof(claim));
        }

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
        Claim = claim;
        SizeObserved = sizeObserved;
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public LedgerClaim? Claim { get; }

    public long? SizeObserved { get; }

    public static LedgerFile StillWriting(RecordingId id, OutputRoot root, RecordingFileName fileName)
        => new(id, root, fileName, null, null);

    public static LedgerFile Ended(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        LedgerClaim claim,
        long sizeObserved)
        => new(id, root, fileName, claim, sizeObserved);
}
