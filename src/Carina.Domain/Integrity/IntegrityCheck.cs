using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public sealed class IntegrityCheck
{
    private IntegrityCheck()
    {
    }

    public IntegrityCheckId Id { get; private set; } = null!;

    public DateTime StartedAt { get; private set; }

    public DateTime FinishedAt { get; private set; }

    public int RootsWalked { get; private set; }

    public int RootsOutOfReach { get; private set; }

    public int FilesRead { get; private set; }

    public int LedgerRowsRead { get; private set; }

    public int LedgerRowsJudged { get; private set; }

    public int LedgerRowsStillWriting { get; private set; }

    public int LedgerRowsInRootsOutOfReach { get; private set; }

    public static IntegrityCheck Rehydrate(
        IntegrityCheckId id,
        DateTime startedAt,
        DateTime finishedAt,
        int rootsWalked,
        int rootsOutOfReach,
        int filesRead,
        int ledgerRowsRead,
        int ledgerRowsJudged,
        int ledgerRowsStillWriting,
        int ledgerRowsInRootsOutOfReach)
    {
        ArgumentNullException.ThrowIfNull(id);

        DateTime began = UtcTimes.Required(startedAt, nameof(startedAt));
        DateTime ended = UtcTimes.Required(finishedAt, nameof(finishedAt));

        if (ended < began)
        {
            throw new ArgumentException("A check finishes after it starts.", nameof(finishedAt));
        }

        return new IntegrityCheck
        {
            Id = id,
            StartedAt = began,
            FinishedAt = ended,
            RootsWalked = Counted(rootsWalked, nameof(rootsWalked)),
            RootsOutOfReach = Counted(rootsOutOfReach, nameof(rootsOutOfReach)),
            FilesRead = Counted(filesRead, nameof(filesRead)),
            LedgerRowsRead = Counted(ledgerRowsRead, nameof(ledgerRowsRead)),
            LedgerRowsJudged = Counted(ledgerRowsJudged, nameof(ledgerRowsJudged)),
            LedgerRowsStillWriting = Counted(ledgerRowsStillWriting, nameof(ledgerRowsStillWriting)),
            LedgerRowsInRootsOutOfReach = Counted(
                ledgerRowsInRootsOutOfReach,
                nameof(ledgerRowsInRootsOutOfReach)),
        };
    }

    private static int Counted(int value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "A check counts nothing negative.");
}
