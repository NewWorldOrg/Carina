using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamScramblingTests
{
    private const long Packets = 1_000_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = Airs.AddMinutes(31);

    [Fact]
    public async Task ARecordingLeftScrambledPastTheLevelEndsWithThatClassInItsDetail()
    {
        Recording read = await Settled(scrambled: 600_000);

        OutcomeDetail scrambling = Assert.Single(read.OutcomeDetail);

        Assert.Equal(RecordingFault.ScramblingUnresolved, scrambling.Fault);
        Assert.Equal(Ended, scrambling.NoticedAt);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task ARecordingScrambledOnlyUnderTheLevelEndsWithNoClassForIt()
    {
        Recording read = await Settled(scrambled: 10);

        Assert.Empty(read.OutcomeDetail);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task ARecordingWhoseScramblingWasNeverCountedEndsWithNoClassForIt()
    {
        Recording read = await Settled(scrambled: null);

        Assert.Empty(read.OutcomeDetail);
    }

    [Fact]
    public async Task TheLevelIsTheOneSetForTheQualityLedgerRatherThanOneOfItsOwn()
    {
        HeldQualityThresholds thresholds = new();
        thresholds.Thresholds.Add(Set(QualityThresholdKey.PacketsLeftScrambled, 0.5));
        thresholds.Thresholds.Add(Set(QualityThresholdKey.PacketsLeftScrambledUnwatchable, 0.9));

        Recording asShipped = await Settled(scrambled: 100_000);
        Recording raised = await Settled(scrambled: 100_000, thresholds);

        Assert.Contains(asShipped.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
        Assert.Empty(raised.OutcomeDetail);
    }

    [Fact]
    public async Task ARecordingOverAndUnknownToTheDriverCarriesTheClassWhenItWasLeftScrambled()
    {
        Recording recording = Scrambled(600_000);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public async Task ARecordingRecoveryMarksCarriesTheClassWhenItWasLeftScrambled()
    {
        Recording recording = Scrambled(600_000);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .RecoverAsync(Greeting(), [], Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public async Task ARecordingRecoveryMarksCarriesNoClassWhenItsScramblingStayedUnderTheLevel()
    {
        Recording recording = Scrambled(10);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .RecoverAsync(Greeting(), [], Cancel);

        Assert.DoesNotContain(
            ledger.Read(recording.Id).OutcomeDetail,
            detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    private static Recording Scrambled(long scrambled)
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(DropCounters.Counted(0, Packets), DropTimeline.Unlocated, scrambled, 0, Airs.AddMinutes(29));

        return recording;
    }

    private static QualityThreshold Set(QualityThresholdKey key, double level)
        => QualityThreshold.Declare(key, Threshold.Of(level, level, provisional: true, 0, Airs));

    private static async Task<Recording> Settled(long? scrambled, HeldQualityThresholds? thresholds = null)
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(DropCounters.Counted(0, Packets), DropTimeline.Unlocated, scrambled, 0, Airs.AddMinutes(29));
        recording.Abort(Airs.AddMinutes(30));

        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording);

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Ended),
                new WeighedFiles { Weighs = 3_400_000_000 },
                thresholds: thresholds)
            .WatchAsync(Cancel);

        return ledger.Read(recording.Id);
    }
}
