using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class TunerStandingTests
{
    private static readonly QualityTally NothingRecorded = QualityAggregator.Tally([]);

    private static readonly QualityTally RecordedWell = QualityAggregator.Tally([QualityFactory.Measured(0.0001)]);

    private static readonly QualityTally RecordedBeyondWarning = QualityAggregator.Tally([QualityFactory.Measured(0.0005)]);

    private static readonly QualityTally RecordedUnwatchable = QualityAggregator.Tally([QualityFactory.Measured(0.005)]);

    private static readonly QualityTally RecordedUnmeasured = QualityAggregator.Tally([QualityFactory.Unmeasured()]);

    private static readonly QualityReading SignalGood = QualityReading.Of(1, 1, 0);

    private static readonly QualityReading SignalBeyond = QualityReading.Of(1, 1, 1);

    private static readonly QualityReading SignalUnmeasured = QualityReading.Of(1, 0, 0);

    private static readonly QualityReading SignalNotSupplied = QualityReading.NotSupplied(1, 0);

    private static readonly QualityReading SignalUnsupported = QualityReading.Unsupported();

    [Fact(DisplayName = "BR-QD-017: a bit error rate beyond its level shows on the row even when nothing was recorded")]
    public void ABitErrorRateBeyondItsLevelShowsOnTheRowEvenWhenNothingWasRecorded()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalGood, SignalGood, SignalBeyond], trouble: null);

        Assert.Equal(new TunerStanding(QualityStanding.Warning), standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner that cannot lock stands as may not be watchable, and says why")]
    public void ATunerThatCannotLockStandsAsMayNotBeWatchableAndSaysWhy()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnmeasured, SignalUnmeasured, SignalUnmeasured], TunerTroubleKind.NoLock);

        Assert.Equal(new TunerStanding(QualityStanding.MayNotBeWatchable, TunerTroubleKind.NoLock), standing);
    }

    [Fact(DisplayName = "BR-QD-017: an enabled tuner nobody measured is unmeasured, not nothing to measure")]
    public void AnEnabledTunerNobodyMeasuredIsUnmeasured()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnmeasured, SignalUnmeasured, SignalUnmeasured], trouble: null);

        Assert.Equal(new TunerStanding(QualityStanding.Unmeasured), standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner with nothing to say but readings it cannot take is unmeasured")]
    public void ATunerWithNothingToSayButReadingsItCannotTakeIsUnmeasured()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnsupported], trouble: null);

        Assert.Equal(QualityStanding.Unmeasured, standing.Standing);
    }

    [Fact(DisplayName = "BR-QD-017: the worst band among the recordings and the signal wins")]
    public void TheWorstBandAmongTheRecordingsAndTheSignalWins()
    {
        Assert.Equal(
            QualityStanding.MayNotBeWatchable,
            TunerStandings.Of([RecordedUnwatchable], [SignalBeyond], trouble: null).Standing);
        Assert.Equal(
            QualityStanding.Warning,
            TunerStandings.Of([RecordedBeyondWarning], [SignalGood], trouble: null).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: a reading beyond its level outweighs a supply that went quiet, which outweighs what was measured well")]
    public void AReadingBeyondItsLevelOutweighsAQuietSupplyWhichOutweighsWhatWasMeasuredWell()
    {
        Assert.Equal(
            QualityStanding.Warning,
            TunerStandings.Of([RecordedUnmeasured], [SignalNotSupplied, SignalBeyond], trouble: null).Standing);
        Assert.Equal(
            QualityStanding.Unreachable,
            TunerStandings.Of([RecordedWell], [SignalNotSupplied, SignalGood], trouble: null).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner is unmeasured only when nothing on it was measured")]
    public void ATunerIsUnmeasuredOnlyWhenNothingOnItWasMeasured()
    {
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedUnmeasured], [SignalGood, SignalUnmeasured], trouble: null).Standing);
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedWell], [SignalUnmeasured], trouble: null).Standing);
        Assert.Equal(
            QualityStanding.Unmeasured,
            TunerStandings.Of([RecordedUnmeasured], [SignalUnmeasured, SignalUnsupported], trouble: null).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: nothing to measure and a reading it cannot take leave a healthy tuner healthy")]
    public void NothingToMeasureAndAReadingItCannotTakeLeaveAHealthyTunerHealthy()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalGood, SignalUnsupported], trouble: null);

        Assert.Equal(new TunerStanding(QualityStanding.Good), standing);
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedWell], [SignalGood], trouble: null).Standing);
    }

    [Fact(DisplayName = "BR-QD-018: a tuner taken out of service for any trouble may not be watchable, whatever was measured")]
    public void ATunerTakenOutOfServiceForAnyTroubleMayNotBeWatchable()
    {
        TunerStanding standing = TunerStandings.Of([RecordedWell], [SignalGood], TunerTroubleKind.DeviceFailed);

        Assert.Equal(new TunerStanding(QualityStanding.MayNotBeWatchable, TunerTroubleKind.DeviceFailed), standing);
        Assert.False(standing.CannotLock);
    }

    [Fact(DisplayName = "BR-QD-018: a tuner failing to tune stands at least at the warning, and at worse when it measured worse")]
    public void ATunerFailingToTuneStandsAtLeastAtTheWarning()
    {
        Assert.Equal(
            new TunerStanding(QualityStanding.Warning, TunerTroubleKind.TuneFailing),
            TunerStandings.Of([NothingRecorded], [SignalUnmeasured], TunerTroubleKind.TuneFailing));
        Assert.Equal(
            new TunerStanding(QualityStanding.Warning, TunerTroubleKind.Degraded),
            TunerStandings.Of([RecordedWell], [SignalGood], TunerTroubleKind.Degraded));
        Assert.Equal(
            new TunerStanding(QualityStanding.MayNotBeWatchable, TunerTroubleKind.TuneFailing),
            TunerStandings.Of([RecordedUnwatchable], [SignalGood], TunerTroubleKind.TuneFailing));
    }

    [Fact(DisplayName = "BR-QD-017: worst first puts a tuner that cannot lock on top and the ones nobody measured last")]
    public void WorstFirstPutsATunerThatCannotLockOnTopAndTheOnesNobodyMeasuredLast()
    {
        TunerStanding[] shuffled =
        [
            new(QualityStanding.Unmeasured),
            new(QualityStanding.Good),
            new(QualityStanding.Warning, TunerTroubleKind.TuneFailing),
            new(QualityStanding.Unreachable),
            new(QualityStanding.MayNotBeWatchable, TunerTroubleKind.NoLock),
            new(QualityStanding.Warning),
            new(QualityStanding.MayNotBeWatchable, TunerTroubleKind.LedgerDisagrees),
            new(QualityStanding.MayNotBeWatchable),
        ];

        Assert.Equal(
            [
                new TunerStanding(QualityStanding.MayNotBeWatchable, TunerTroubleKind.NoLock),
                new TunerStanding(QualityStanding.MayNotBeWatchable, TunerTroubleKind.LedgerDisagrees),
                new TunerStanding(QualityStanding.MayNotBeWatchable),
                new TunerStanding(QualityStanding.Warning, TunerTroubleKind.TuneFailing),
                new TunerStanding(QualityStanding.Warning),
                new TunerStanding(QualityStanding.Unreachable),
                new TunerStanding(QualityStanding.Good),
                new TunerStanding(QualityStanding.Unmeasured),
            ],
            shuffled.OrderByDescending(TunerStandings.Severity));
    }
}
