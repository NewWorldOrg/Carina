using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record LedgerFile
{
    private LedgerFile(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        LedgerClaim? claim,
        long? sizeObserved,
        bool thumbnailDrawn)
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
        ThumbnailDrawn = thumbnailDrawn;
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public LedgerClaim? Claim { get; }

    public long? SizeObserved { get; }

    /// <summary>
    /// Whether the row says the recording's thumbnail has been drawn.
    /// </summary>
    public bool ThumbnailDrawn { get; }

    /// <summary>
    /// What a recording's row says of its file: still being written while it has no outcome, and otherwise what its
    /// outcome says landed, the size read off the disk as it ended and whether the row says its picture is ready.
    /// </summary>
    /// <exception cref="InvalidOperationException">The recording ended without a size read off the disk.</exception>
    public static LedgerFile Of(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        RecordingOutcome? outcome,
        long? sizeObserved,
        ThumbnailState thumbnail)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (outcome is not { } ended)
        {
            return StillWriting(id, root, fileName);
        }

        if (sizeObserved is not { } observed)
        {
            throw new InvalidOperationException(
                $"Recording {id.Wire} ended {ended} without a size read off the disk.");
        }

        return Ended(id, root, fileName, Claimed(ended), observed, thumbnail is ThumbnailState.Ready);
    }

    public static LedgerFile StillWriting(RecordingId id, OutputRoot root, RecordingFileName fileName)
        => new(id, root, fileName, null, null, false);

    public static LedgerFile Ended(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        LedgerClaim claim,
        long sizeObserved,
        bool thumbnailDrawn = false)
        => new(id, root, fileName, claim, sizeObserved, thumbnailDrawn);

    private static LedgerClaim Claimed(RecordingOutcome outcome)
        => outcome switch
        {
            RecordingOutcome.Complete => LedgerClaim.EverythingLanded,
            RecordingOutcome.Truncated => LedgerClaim.SomethingLanded,
            RecordingOutcome.Failed => LedgerClaim.NothingLanded,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A recording ends in one of three ways."),
        };
}
