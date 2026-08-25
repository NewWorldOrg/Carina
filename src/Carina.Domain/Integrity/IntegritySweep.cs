using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public sealed class IntegritySweep
{
    private IntegritySweep(
        DateTime ranAt,
        int rootsWalked,
        int rootsOutOfReach,
        int filesRead,
        int ledgerRowsRead,
        int ledgerRowsJudged,
        int ledgerRowsStillWriting,
        int ledgerRowsInRootsOutOfReach,
        IReadOnlyList<IntegrityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        RanAt = UtcTimes.Required(ranAt, nameof(ranAt));
        RootsWalked = Counted(rootsWalked, nameof(rootsWalked));
        RootsOutOfReach = Counted(rootsOutOfReach, nameof(rootsOutOfReach));
        FilesRead = Counted(filesRead, nameof(filesRead));
        LedgerRowsRead = Counted(ledgerRowsRead, nameof(ledgerRowsRead));
        LedgerRowsJudged = Counted(ledgerRowsJudged, nameof(ledgerRowsJudged));
        LedgerRowsStillWriting = Counted(ledgerRowsStillWriting, nameof(ledgerRowsStillWriting));
        LedgerRowsInRootsOutOfReach = Counted(
            ledgerRowsInRootsOutOfReach,
            nameof(ledgerRowsInRootsOutOfReach));
        Findings = [.. findings];
    }

    public DateTime RanAt { get; }

    public int RootsWalked { get; }

    public int RootsOutOfReach { get; }

    public int FilesRead { get; }

    public int LedgerRowsRead { get; }

    public int LedgerRowsJudged { get; }

    public int LedgerRowsStillWriting { get; }

    public int LedgerRowsInRootsOutOfReach { get; }

    public IReadOnlyList<IntegrityFinding> Findings { get; }

    public static IntegritySweep Of(
        DateTime ranAt,
        int rootsWalked,
        int rootsOutOfReach,
        int filesRead,
        int ledgerRowsRead,
        int ledgerRowsJudged,
        int ledgerRowsStillWriting,
        int ledgerRowsInRootsOutOfReach,
        IReadOnlyList<IntegrityFinding> findings)
        => new(
            ranAt,
            rootsWalked,
            rootsOutOfReach,
            filesRead,
            ledgerRowsRead,
            ledgerRowsJudged,
            ledgerRowsStillWriting,
            ledgerRowsInRootsOutOfReach,
            findings);

    private static int Counted(int value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "A sweep counts nothing negative.");
}
