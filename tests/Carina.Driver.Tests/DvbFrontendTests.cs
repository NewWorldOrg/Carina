using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbFrontendTests
{
    private const string Path = "/dev/dvb/adapter0/frontend0";

    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    [Fact]
    public void AFrontendThatCannotBeOpenedNamesTheNodeAndTheErrno()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.RefuseToOpen(Path, Errno.NoSuchDevice);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbFrontend.Open(calls, Path, DvbAccess.Control)
        );

        Assert.Contains(Path, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("opening the frontend", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("errno 19", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrontendAnotherProcessHoldsSaysSoRatherThanBlamingThePath()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.RefuseToOpen(Path, Errno.Busy);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbFrontend.Open(calls, Path, DvbAccess.Control)
        );

        Assert.Contains("already holding this tuner", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TuningWritesThePropertyListToTheFrontendItOpened()
    {
        var calls = new ScriptedDvbSystemCalls();
        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        frontend.Tune(DvbChannel.Terrestrial(55));

        var written = Assert.Single(calls.PropertiesSet);
        Assert.Equal(DvbProperty.Clear, written.PropertyAt(0));
        Assert.Equal(DvbProperty.Tune, written.PropertyAt(written.Count - 1));
    }

    [Fact]
    public void ATuneThatTheKernelRefusesNamesTheChannelAndTheDevice()
    {
        var calls = new ScriptedDvbSystemCalls { RefusePropertySetWith = Errno.NoSuchDevice };
        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => frontend.Tune(DvbChannel.Terrestrial(55))
        );

        Assert.Contains(Path, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("55", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("errno 19", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatusThatCannotBeReadIsNeverReportedAsUnlocked()
    {
        var calls = new ScriptedDvbSystemCalls { RefuseStatusWith = Errno.NoSuchDevice };
        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        var refusal = Assert.Throws<DvbDeviceException>(() => frontend.Status());

        Assert.Contains("reading the frontend status", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AVoltageTheKernelRefusesStopsTheSatelliteTune()
    {
        var calls = new ScriptedDvbSystemCalls { RefuseVoltageWith = Errno.NoSuchDevice };
        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => frontend.SetLnbVoltage(LnbVoltage.Off)
        );

        Assert.Contains("aerial supply", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingForLockStopsAsSoonAsTheFrontendLocks()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatusesInTurn(FrontendStatus.None, FrontendStatus.Signal, Locked);

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        Assert.True(
            frontend.WaitForLock(
                clock,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                out _
            )
        );
        Assert.Equal(TimeSpan.FromMilliseconds(200), calls.RestedFor);
    }

    [Fact]
    public void WaitingForLockGivesUpOnceItsPatienceHasRunOut()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(FrontendStatus.Signal);

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        Assert.False(
            frontend.WaitForLock(
                clock,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                out _
            )
        );
        Assert.True(calls.RestedFor >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WaitingForLockStopsWhenTheSessionIsCancelled()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(FrontendStatus.None);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        Assert.Throws<OperationCanceledException>(
            () =>
                frontend.WaitForLock(
                    clock,
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(100),
                    cancellation.Token,
                    out _
                )
        );
    }

    [Fact]
    public void ALockedFrontendReportsAQualityBuiltFromAllThreeStatistics()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.ReportStatus(Locked);
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, 20_500)]
        );
        calls.AnswerWith(
            DvbProperty.PostErrorBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 3)]
        );
        calls.AnswerWith(
            DvbProperty.PostTotalBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 30_000)]
        );

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);
        var quality = frontend.Quality();

        Assert.True(quality.HasLock);
        Assert.True(quality.CarrierToNoise.TryGetDecibels(out var decibels));
        Assert.Equal(20.5, decibels, 3);
        Assert.Equal(SignalReading.Measured, quality.PostViterbiErrors.Reading);
    }

    [Fact]
    public void AnUnlockedFrontendReportsNoCarrierToNoiseEvenThoughTheKernelGaveOne()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.ReportStatus(FrontendStatus.Signal);
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, -33_674)]
        );

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);
        var quality = frontend.Quality();

        Assert.False(quality.HasLock);
        Assert.Equal(SignalReading.FrontendNotLocked, quality.CarrierToNoise.Reading);
        Assert.False(quality.CarrierToNoise.TryGetDecibels(out _));
    }

    [Fact]
    public void AFrontendThatDropsLockBetweenTheTwoStatusReadsYieldsNoMeasurement()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.ReportStatusesInTurn(Locked, FrontendStatus.Signal);
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, -33_674)]
        );
        calls.AnswerWith(
            DvbProperty.PostErrorBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 3)]
        );
        calls.AnswerWith(
            DvbProperty.PostTotalBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 30_000)]
        );

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);
        var quality = frontend.Quality();

        Assert.False(quality.HasLock);
        Assert.Equal(SignalReading.UnavailableRightNow, quality.CarrierToNoise.Reading);
        Assert.False(quality.CarrierToNoise.TryGetDecibels(out _));
        Assert.Equal(SignalReading.UnavailableRightNow, quality.PostViterbiErrors.Reading);
    }

    [Fact]
    public void AFrontendThatGainsLockBetweenTheTwoStatusReadsYieldsNoMeasurement()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.ReportStatusesInTurn(FrontendStatus.Signal, Locked);
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, 20_500)]
        );

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);
        var quality = frontend.Quality();

        Assert.False(quality.HasLock);
        Assert.Equal(SignalReading.UnavailableRightNow, quality.CarrierToNoise.Reading);
    }

    [Fact]
    public void ReadingQualityAsksForTheStatusOnBothSidesOfTheStatistics()
    {
        var calls = new ScriptedDvbSystemCalls();
        calls.ReportStatus(Locked);

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);
        frontend.Quality();

        Assert.Equal(2, calls.StatusReads);
    }

    [Fact]
    public void StatisticsTheKernelWillNotAnswerBecomeARefusalNotAZero()
    {
        var calls = new ScriptedDvbSystemCalls { RefuseProperty = DvbProperty.CarrierToNoise };
        calls.ReportStatus(Locked);

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        var refusal = Assert.Throws<DvbDeviceException>(() => frontend.Quality());

        Assert.Contains("signal statistics", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Path, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliverySystemsComeBackFromTheEnumeration()
    {
        var calls = new ScriptedDvbSystemCalls
        {
            DeliverySystems = [DeliverySystem.IsdbTerrestrial],
        };

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Inspect);

        Assert.True(frontend.TryReadDeliverySystems(out var systems, out _));
        Assert.Equal([DeliverySystem.IsdbTerrestrial], systems);
    }

    [Fact]
    public void AFrontendThatEnumeratesNothingIsAProblemRatherThanAnEmptyAnswer()
    {
        var calls = new ScriptedDvbSystemCalls { DeliverySystems = [] };

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Inspect);

        Assert.False(frontend.TryReadDeliverySystems(out var systems, out var problem));
        Assert.Empty(systems);
        Assert.Contains("no delivery systems", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHardwareNameStopsAtItsTerminator()
    {
        var calls = new ScriptedDvbSystemCalls { HardwareName = "PT3 ISDB-S" };

        using var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Inspect);

        Assert.True(frontend.TryReadHardwareName(out var name, out _));
        Assert.Equal("PT3 ISDB-S", name);
    }

    [Fact]
    public void ClosingTheFrontendHandsTheDescriptorBackExactlyOnce()
    {
        var calls = new ScriptedDvbSystemCalls();
        var frontend = DvbFrontend.Open(calls, Path, DvbAccess.Control);

        frontend.Dispose();
        frontend.Dispose();

        Assert.Single(calls.Closed);
    }
}
