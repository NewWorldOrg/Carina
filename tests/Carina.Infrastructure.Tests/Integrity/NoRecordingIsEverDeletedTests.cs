using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class NoRecordingIsEverDeletedTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    [Fact]
    public async Task ASweepOverEveryKindOfTroubleLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();
        using var state = new TempTree();

        recordings
            .Holding("agrees.m2ts", 400)
            .Holding("disagrees.m2ts", 300)
            .Holding("empty.m2ts", 0)
            .Holding("stray.m2ts", 200)
            .Holding("still-writing.m2ts", 100)
            .HoldingDirectory("thumbnails");

        IReadOnlyList<string> before = recordings.Snapshot();

        var ledger = new HeldLedger(
            LedgerFile.Ended(Id(1), Primary, new RecordingFileName("agrees.m2ts"), 400),
            LedgerFile.Ended(Id(2), Primary, new RecordingFileName("disagrees.m2ts"), 999),
            LedgerFile.Ended(Id(3), Primary, new RecordingFileName("empty.m2ts"), 500),
            LedgerFile.Ended(Id(4), Primary, new RecordingFileName("gone.m2ts"), 600),
            LedgerFile.StillWriting(Id(5), Primary, new RecordingFileName("still-writing.m2ts")));

        using IntegrityCheckJob job = Job(ledger, recordings, state);
        IntegrityRun run = await job.RunAsync(Cancel);

        IntegritySweep swept = Assert.IsType<IntegritySweep>(run.Swept);

        Assert.Equal(
            ["FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            swept.Findings.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(4, swept.LedgerRowsJudged);
        Assert.Equal(5, swept.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(6, before.Count);
    }

    [Fact]
    public async Task ASweepThatFindsNothingWrongStillLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();
        using var state = new TempTree();

        recordings.Holding("agrees.m2ts", 400);

        IReadOnlyList<string> before = recordings.Snapshot();

        var ledger = new HeldLedger(
            LedgerFile.Ended(Id(1), Primary, new RecordingFileName("agrees.m2ts"), 400));

        using IntegrityCheckJob job = Job(ledger, recordings, state);
        IntegritySweep swept = (await job.RunAsync(Cancel)).Swept!;

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Single(before);
    }

    [Fact]
    public async Task SweepingTheSameTroubleAgainAndAgainStillLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();
        using var state = new TempTree();

        recordings.Holding("empty.m2ts", 0).Holding("stray.m2ts", 200);

        IReadOnlyList<string> before = recordings.Snapshot();

        var ledger = new HeldLedger(
            LedgerFile.Ended(Id(3), Primary, new RecordingFileName("empty.m2ts"), 500));

        using IntegrityCheckJob job = Job(ledger, recordings, state);

        for (int round = 0; round < 5; round++)
        {
            Assert.Equal(2, (await job.RunAsync(Cancel)).Swept!.Findings.Count);
        }

        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(2, before.Count);
    }

    [Fact]
    public async Task TheSweepWritesItsReportSomewhereThatIsNotTheRecordingStore()
    {
        using var recordings = new TempTree();
        using var state = new TempTree();

        recordings.Holding("agrees.m2ts", 400);

        IReadOnlyList<string> before = recordings.Snapshot();

        using IntegrityCheckJob job = Job(new HeldLedger(), recordings, state);
        await job.RunAsync(Cancel);

        Assert.Equal(before, recordings.Snapshot());
        Assert.NotEmpty(state.Snapshot());
    }

    private static IntegrityCheckJob Job(HeldLedger ledger, TempTree recordings, TempTree state)
    {
        var settings = new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Primary, recordings.Root)],
            ReportPath = state.Under("report.json"),
        };

        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance),
            new JsonIntegrityReportStore(settings),
            settings,
            new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);
    }
}
