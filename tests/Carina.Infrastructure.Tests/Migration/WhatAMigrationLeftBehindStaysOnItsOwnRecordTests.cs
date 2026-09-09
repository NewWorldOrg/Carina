using Carina.Domain.Integrity;
using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class WhatAMigrationLeftBehindStaysOnItsOwnRecordTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string from = Directory.CreateTempSubdirectory("carina-left-behind-from").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-left-behind-into").FullName;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly HeldMigrationRecords records = new();

    private readonly OneAtATime lease = new();

    private readonly MigrationBench bench;

    private readonly HeldMigratedRecordings recordings;

    public WhatAMigrationLeftBehindStaysOnItsOwnRecordTests()
    {
        bench = new MigrationBench(clock);
        recordings = new HeldMigratedRecordings(bench.Journal);

        File.WriteAllText(Path.Combine(from, "one.m2ts"), "a recording");
        File.WriteAllText(Path.Combine(from, "notes.txt"), "not a recording at all");
        File.WriteAllText(Path.Combine(from, "empty.m2ts"), string.Empty);
    }

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact]
    public async Task NothingAMigrationRefusedTurnsUpAsAnAnomalyTheNewSystemFound()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        MigrationReport told = records.Saved.Single();

        Assert.NotEmpty(told.Details);

        IntegrityReport swept = IntegrityScan.Compare(
            IntegrityCheckId.New(),
            [.. recordings.Written.Select(Weighed)],
            [RootListing.Of(Root, [.. Directory.GetFiles(into).Select(Listed)])],
            Began,
            Ended);

        Assert.Empty(swept.Findings);
        Assert.Equal(recordings.Written.Count, swept.Check.LedgerRowsJudged);
        Assert.Equal(Directory.GetFiles(into).Length, swept.Check.FilesRead);
    }

    private static LedgerFile Weighed(Recording recording)
        => LedgerFile.Ended(
            recording.Id,
            recording.OutputRoot,
            recording.FileName,
            LedgerClaim.SomethingLanded,
            recording.FileSizeObserved ?? 0);

    private static StoredFile Listed(string path) => new(Path.GetFileName(path), new FileInfo(path).Length);

    private MigrationPassage Passage()
        => new(
            new SourceOnDisk(from),
            new SourceOnDisk(from),
            bench.Carriage(new HardLinkMigrationCarrier(from, into, Root), recordings),
            records,
            lease,
            clock);
}
