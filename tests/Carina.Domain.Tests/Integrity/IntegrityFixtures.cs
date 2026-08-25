using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Integrity;

internal static class IntegrityFixtures
{
    public static readonly DateTime At = new(2026, 8, 26, 3, 0, 0, DateTimeKind.Utc);

    public static readonly OutputRoot Primary = new("primary");

    public static readonly OutputRoot Bulk = new("bulk");

    public static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    public static LedgerFile Ended(OutputRoot root, string fileName, long sizeObserved, int seed = 1)
        => LedgerFile.Ended(Id(seed), root, new RecordingFileName(fileName), sizeObserved);

    public static LedgerFile StillWriting(OutputRoot root, string fileName, int seed = 1)
        => LedgerFile.StillWriting(Id(seed), root, new RecordingFileName(fileName));

    public static RootListing Holding(OutputRoot root, params (string Name, long SizeBytes)[] files)
        => RootListing.Of(root, [.. files.Select(file => new StoredFile(file.Name, file.SizeBytes))]);

    public static RootListing Empty(OutputRoot root) => RootListing.Of(root, []);
}
