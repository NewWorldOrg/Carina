using Carina.Contracts;
using Carina.Driver.Ipc;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class AFrontendThatWillNotMeasureTests
{
    private const string FrontendPath = "/dev/dvb/adapter0/frontend0";

    private const int LegacyNotSupported = 524;

    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    private const FrontendStatus CarrierOnly = FrontendStatus.Signal;

    private static readonly DvbDevicePaths Paths = new(
        FrontendPath,
        "/dev/dvb/adapter0/demux0",
        "/dev/dvb/adapter0/dvr0"
    );

    private static readonly DvbTunerSettings Settings = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(5),
        8 * 1024 * 1024
    );

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    [Fact]
    public void StatisticsRefusedWithAnErrnoTheRuntimeCannotNameStillCarryTheNumber()
    {
        var calls = new ScriptedDvbSystemCalls
        {
            RefuseProperty = DvbProperty.CarrierToNoise,
            RefusePropertyWith = LegacyNotSupported,
        };
        calls.ReportStatus(Locked);

        using var frontend = DvbFrontend.Open(calls, FrontendPath, DvbAccess.Control);

        DvbDeviceException refusal = Assert.Throws<DvbDeviceException>(() => frontend.Quality());

        Assert.Equal(LegacyNotSupported, refusal.Error);
        Assert.Equal(TuningFailure.DeviceUnusable, refusal.Failure);
        Assert.Contains("errno 524", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrontendThatRefusesItsStatisticsReachesTheAppAsAReadingThatCouldNotBeTaken()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);

        using DvbTunerDevice device = Open(calls, clock);

        calls.RefuseProperty = DvbProperty.CarrierToNoise;
        calls.RefusePropertyWith = LegacyNotSupported;

        var problems = new List<Exception>();
        SignalQualityDto reading = Read(device, clock, problems);

        Assert.Equal(SignalLock.Unspecified, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Empty(reading.PostViterbiBitErrors);
        Assert.Empty(reading.NotImplementedMetrics);
        Assert.Empty(reading.MetricsOnAnotherScale);
        Assert.Single(problems);
    }

    [Fact]
    public void AStatisticThisDriverNeverAsksForCannotStopItFromReadingTheOnesItDoes()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);
        Measuring(calls);

        using DvbTunerDevice device = Open(calls, clock);

        calls.RefuseProperty = DvbProperty.StreamId;
        calls.RefusePropertyWith = LegacyNotSupported;

        var problems = new List<Exception>();
        SignalQualityDto reading = Read(device, clock, problems);

        Assert.Empty(problems);
        Assert.Equal(SignalLock.Locked, reading.Lock);
        Assert.Equal(21_500, reading.CnrMilliDecibels);
    }

    [Fact]
    public void ATunerThatKeepsNoCarrierToNoiseNamesThatStatisticAndStillCountsItsBitErrors()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);
        calls.AnswerWith(
            DvbProperty.PostErrorBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 4)]
        );
        calls.AnswerWith(
            DvbProperty.PostTotalBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 2_000_000)]
        );

        using DvbTunerDevice device = Open(calls, clock);

        SignalQualityDto reading = Read(device, clock, []);

        Assert.Equal(SignalLock.Locked, reading.Lock);
        Assert.Equal([SignalQualityMetrics.Cnr], reading.NotImplementedMetrics);
        Assert.False(reading.Implements(SignalQualityMetrics.Cnr));
        Assert.True(reading.Implements(SignalQualityMetrics.PostViterbiBitError));
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Equal(
            [new LayerBitErrorCounts(0, 4, 2_000_000)],
            reading.PostViterbiBitErrors
        );
    }

    [Fact]
    public void AnUnlockedFrontendsNegativeCarrierToNoiseReachesTheAppAsNoFigureAtAll()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, -45_500)]
        );
        calls.AnswerWith(
            DvbProperty.PostErrorBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 4)]
        );
        calls.AnswerWith(
            DvbProperty.PostTotalBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 2_000_000)]
        );

        using DvbTunerDevice device = Open(calls, clock);

        calls.ReportStatus(CarrierOnly);

        SignalQualityDto reading = Read(device, clock, []);

        Assert.Equal(SignalLock.NotLocked, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Empty(reading.PostViterbiBitErrors);
        Assert.Empty(reading.NotImplementedMetrics);
        Assert.NotNull(reading.LockReadAt);
    }

    private static void Measuring(ScriptedDvbSystemCalls calls)
    {
        calls.AnswerWith(
            DvbProperty.CarrierToNoise,
            [new DvbStatisticLayer(StatisticScale.Decibel, 21_500)]
        );
        calls.AnswerWith(
            DvbProperty.PostErrorBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 4)]
        );
        calls.AnswerWith(
            DvbProperty.PostTotalBitCount,
            [new DvbStatisticLayer(StatisticScale.Counter, 2_000_000)]
        );
    }

    private static SignalQualityDto Read(
        DvbTunerDevice device,
        ManualTimeProvider clock,
        List<Exception> problems
    )
    {
        ISignalQualitySource source = Assert.IsAssignableFrom<ISignalQualitySource>(device.Quality);
        var reader = new SignalQualityReader(source, clock, Interval, problem: problems.Add);

        return SignalQualityViews.Of(reader.Read());
    }

    private static DvbTunerDevice Open(ScriptedDvbSystemCalls calls, ManualTimeProvider clock) =>
        DvbTunerDevice.Open(
            calls,
            clock,
            Paths,
            DvbChannel.Terrestrial(55),
            LnbVoltage.Off,
            Settings,
            CancellationToken.None
        );
}
