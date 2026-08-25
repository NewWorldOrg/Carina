using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Integrity;

internal static class IntegrityFixtures
{
    public static readonly DateTime At = new(2026, 8, 26, 3, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Done = new(2026, 8, 26, 3, 0, 2, DateTimeKind.Utc);

    public static readonly OutputRoot Primary = new("primary");

    public static readonly OutputRoot Bulk = new("bulk");

    public static readonly IntegrityCheckId Check = new(new Guid("9f2b7c10-0000-0000-0000-000000000001"));

    public static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    public static LedgerFile Complete(OutputRoot root, string fileName, long sizeObserved, int seed = 1)
        => Ended(root, fileName, LedgerClaim.EverythingLanded, sizeObserved, seed);

    public static LedgerFile Truncated(OutputRoot root, string fileName, long sizeObserved, int seed = 1)
        => Ended(root, fileName, LedgerClaim.SomethingLanded, sizeObserved, seed);

    public static LedgerFile Failed(OutputRoot root, string fileName, long sizeObserved, int seed = 1)
        => Ended(root, fileName, LedgerClaim.NothingLanded, sizeObserved, seed);

    public static LedgerFile Ended(
        OutputRoot root,
        string fileName,
        LedgerClaim claim,
        long sizeObserved,
        int seed = 1)
        => LedgerFile.Ended(Id(seed), root, new RecordingFileName(fileName), claim, sizeObserved);

    public static LedgerFile StillWriting(OutputRoot root, string fileName, int seed = 1)
        => LedgerFile.StillWriting(Id(seed), root, new RecordingFileName(fileName));

    public static RootListing Holding(OutputRoot root, params (string Path, long SizeBytes)[] files)
        => RootListing.Of(root, [.. files.Select(file => new StoredFile(file.Path, file.SizeBytes))]);

    public static RootListing Empty(OutputRoot root) => RootListing.Of(root, []);

    public static IntegrityReport Compare(
        IReadOnlyList<LedgerFile> ledger,
        IReadOnlyList<RootListing> listings)
        => IntegrityScan.Compare(Check, ledger, listings, At, Done);
}
