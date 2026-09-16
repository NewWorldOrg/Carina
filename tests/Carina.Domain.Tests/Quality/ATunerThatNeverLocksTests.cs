using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class ATunerThatNeverLocksTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Watched = TimeSpan.FromHours(15);

    private static readonly TimeSpan Between = TimeSpan.FromSeconds(10);

    private static readonly TunerDeviceId Tuner = new("adapter0.frontend0");

    private static readonly IReadOnlyList<QualityThresholdStanding> Levels =
        QualityThresholdStanding.Over([], Noon);

    [Fact(DisplayName = "BR-QD-004: a tuner that answered for fifteen hours without once locking reads as beyond the level, never as unmeasured")]
    public void ATunerThatAnsweredForFifteenHoursWithoutOnceLockingReadsAsBeyondTheLevel()
    {
        QualitySignalRead read = Read(QualityThresholdKey.LockRate, QualitySignalSurvey.Figures(Hours(), []));

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(1, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "BR-QD-001: the carrier to noise of a tuner that never locked reads as unmeasured rather than as a cold figure it never had")]
    public void TheCarrierToNoiseOfATunerThatNeverLockedReadsAsUnmeasured()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures([], Unlocked());

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(5_400, figure.Samples);
        Assert.Equal(0, figure.Locked);
        Assert.Equal(0d, figure.LockRate);
        Assert.Null(figure.CarrierToNoiseLowest);

        Assert.Equal(
            QualityState.Unmeasured,
            QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
        Assert.Equal(
            QualityState.AtOrAboveWarning,
            QualityStates.Of(Read(QualityThresholdKey.LockRate, figures).Reading));
    }

    [Fact(DisplayName = "BR-QD-007: a tuner that keeps answering while it never locks is not a supply that went quiet")]
    public void ATunerThatKeepsAnsweringWhileItNeverLocksIsNotASupplyThatWentQuiet()
    {
        TimeSpan longest = TimeSpan.FromSeconds(QualityThresholdShapes.Of(QualityThresholdKey.SupplySilence).Shipped);

        Assert.Empty(SupplyWatch.Quiet([Heard(Noon - Between)], longest, Noon));
        Assert.Single(SupplyWatch.Quiet([Heard(Noon - Watched)], longest, Noon));
    }

    private static SupplyReading Heard(DateTime at)
        => SupplyReading.Of(
            SupplySilence.SignalSamples,
            QualitySubject.Of(QualitySubjectKind.Tuner, Tuner.Value),
            at);

    private static QualitySignalRead Read(QualityThresholdKey key, IReadOnlyList<SignalFigures> figures)
        => QualitySignalSurvey.Read(figures, [Tuner], Levels).Single(one => one.Key == key);

    private static IReadOnlyList<QualitySignalRollup> Hours()
        =>
        [
            .. Enumerable
                .Range(0, (int)Watched.TotalHours)
                .Select(hour => QualitySignalRollup.Rehydrate(
                    QualityWindow.Hour,
                    Noon - Watched + TimeSpan.FromHours(hour),
                    Tuner,
                    new NetworkId(4),
                    new ServiceId(101),
                    360,
                    0,
                    0,
                    0,
                    null,
                    null,
                    null,
                    [])),
        ];

    private static IReadOnlyList<QualitySignalSample> Unlocked()
        =>
        [
            .. Enumerable
                .Range(0, (int)(Watched / Between))
                .Select(step => Noon - Watched + (Between * step))
                .Select(at => QualitySignalSample.Rehydrate(
                    "instance-a",
                    SessionId.Parse("guide-1"),
                    at,
                    SessionPurpose.Survey,
                    Tuner,
                    new NetworkId(4),
                    new ServiceId(101),
                    SignalSample.WithoutLock(at))),
        ];
}
