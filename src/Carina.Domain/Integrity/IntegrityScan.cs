using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public static class IntegrityScan
{
    public static IntegrityReport Compare(
        IntegrityCheckId id,
        IReadOnlyList<LedgerFile> ledger,
        IReadOnlyList<RootListing> listings,
        DateTime startedAt,
        DateTime finishedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(listings);

        Dictionary<string, RootListing> reachable = Reachable(listings, out int outOfReach);
        HashSet<string> claimed = Claimed(ledger);
        List<IntegrityFinding> findings = [];

        int judged = 0;
        int stillWriting = 0;
        int beyondReach = 0;

        foreach (LedgerFile row in ledger)
        {
            if (row.SizeObserved is not { } ledgerSize)
            {
                stillWriting++;
                continue;
            }

            if (!reachable.TryGetValue(row.Root.Value, out RootListing? listing))
            {
                beyondReach++;
                continue;
            }

            judged++;

            if (listing.At(row.FileName.Value) is not { } file)
            {
                findings.Add(
                    IntegrityFinding.FileMissing(id, row.Root, row.Id, row.FileName, ledgerSize, startedAt));
                continue;
            }

            if (file.SizeBytes is 0 && row.Claim is not LedgerClaim.NothingLanded)
            {
                findings.Add(Empty(id, row, ledgerSize, file.SizeBytes, startedAt));
                continue;
            }

            if (ledgerSize != file.SizeBytes)
            {
                findings.Add(
                    IntegrityFinding.SizeDisagrees(
                        id,
                        row.Root,
                        row.Id,
                        row.FileName,
                        ledgerSize,
                        file.SizeBytes,
                        startedAt));
            }
        }

        int filesRead = 0;

        foreach (RootListing listing in reachable.Values)
        {
            foreach (StoredFile file in listing.Files)
            {
                filesRead++;

                if (!claimed.Contains(Key(listing.Root, file.Path)))
                {
                    findings.Add(
                        IntegrityFinding.NoLedgerRow(id, listing.Root, file.Path, file.SizeBytes, startedAt));
                }
            }
        }

        IntegrityCheck check = IntegrityCheck.Rehydrate(
            id,
            startedAt,
            finishedAt,
            reachable.Count,
            outOfReach,
            filesRead,
            ledger.Count,
            judged,
            stillWriting,
            beyondReach);

        return IntegrityReport.Of(check, InAStableOrder(findings));
    }

    private static IntegrityFinding Empty(
        IntegrityCheckId id,
        LedgerFile row,
        long ledgerSize,
        long observedSize,
        DateTime at)
        => row.Claim is LedgerClaim.EverythingLanded
            ? IntegrityFinding.EmptyThoughComplete(
                id,
                row.Root,
                row.Id,
                row.FileName,
                ledgerSize,
                observedSize,
                at)
            : IntegrityFinding.FileEmpty(id, row.Root, row.Id, row.FileName, ledgerSize, observedSize, at);

    private static Dictionary<string, RootListing> Reachable(
        IReadOnlyList<RootListing> listings,
        out int outOfReach)
    {
        Dictionary<string, RootListing> reachable = new(StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        outOfReach = 0;

        foreach (RootListing listing in listings)
        {
            ArgumentNullException.ThrowIfNull(listing);

            if (!seen.Add(listing.Root.Value))
            {
                throw new ArgumentException(
                    $"A sweep walks each output root once, so '{listing.Root.Value}' cannot be listed twice.",
                    nameof(listings));
            }

            if (listing.Reachable)
            {
                reachable[listing.Root.Value] = listing;
            }
            else
            {
                outOfReach++;
            }
        }

        return reachable;
    }

    private static HashSet<string> Claimed(IReadOnlyList<LedgerFile> ledger)
    {
        HashSet<string> claimed = new(StringComparer.Ordinal);

        foreach (LedgerFile row in ledger)
        {
            ArgumentNullException.ThrowIfNull(row);

            if (!claimed.Add(Key(row.Root, row.FileName.Value)))
            {
                throw new ArgumentException(
                    $"The ledger holds one row per file, so '{row.Root.Value}/{row.FileName.Value}' "
                    + "cannot appear twice.",
                    nameof(ledger));
            }
        }

        return claimed;
    }

    private static IReadOnlyList<IntegrityFinding> InAStableOrder(List<IntegrityFinding> findings)
        => [.. findings
            .OrderBy(finding => finding.Root.Value, StringComparer.Ordinal)
            .ThenBy(finding => finding.Path, StringComparer.Ordinal)];

    private static string Key(OutputRoot root, string path) => root.Value + "/" + path;
}
