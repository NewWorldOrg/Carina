using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class QualitySignalWindowReadTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly QualityPeriod Day = QualityPeriod.Of(Noon.AddDays(-1), Noon, Noon)!;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task APeriodIsReadFromTheHourlyWindowsAndTheSamplesNotYetRolledUp()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();
        await rollups.SaveAsync([Rollup(Noon.AddHours(-2))], Cancel);
        samples.Samples.Add(Sample(Noon.AddMinutes(-30)));

        IReadOnlyList<QualitySignalWindow> windows = await new QualitySignalReader(rollups, samples)
            .WindowsAsync(Day, Cancel);

        Assert.Equal([Noon.AddHours(-2), Noon.AddMinutes(-30)], windows.Select(window => window.Start));
        Assert.All(windows, window => Assert.Equal(new ServiceId(101), window.Service));
    }

    [Fact]
    public async Task APeriodThatBeginsInsideAnHourReadsThatHoursRemainderFromTheSamples()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();
        await rollups.SaveAsync([Rollup(Noon.AddHours(-2)), Rollup(Noon.AddHours(-1))], Cancel);
        samples.Samples.Add(Sample(Noon.AddMinutes(-105)));
        samples.Samples.Add(Sample(Noon.AddMinutes(-75)));
        QualityPeriod period = QualityPeriod.Of(Noon.AddMinutes(-90), Noon, Noon)!;

        IReadOnlyList<QualitySignalWindow> windows = await new QualitySignalReader(rollups, samples)
            .WindowsAsync(period, Cancel);

        Assert.Equal([Noon.AddMinutes(-75), Noon.AddHours(-1)], windows.Select(window => window.Start));
    }

    [Fact]
    public async Task APeriodThatEndsInsideAnHourReadsThatHoursBeginningFromTheSamples()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();
        await rollups.SaveAsync(
            [Rollup(Noon.AddHours(-4)), Rollup(Noon.AddHours(-3)), Rollup(Noon.AddHours(-2))],
            Cancel);
        samples.Samples.Add(Sample(Noon.AddMinutes(-105)));
        samples.Samples.Add(Sample(Noon.AddMinutes(-75)));
        QualityPeriod period = QualityPeriod.Of(Noon.AddHours(-4), Noon.AddMinutes(-90), Noon)!;

        IReadOnlyList<QualitySignalWindow> windows = await new QualitySignalReader(rollups, samples)
            .WindowsAsync(period, Cancel);

        Assert.Equal(
            [Noon.AddHours(-4), Noon.AddHours(-3), Noon.AddMinutes(-105)],
            windows.Select(window => window.Start));
    }

    [Fact]
    public async Task APeriodInsideOneHourIsReadWhollyFromTheSamples()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();
        await rollups.SaveAsync([Rollup(Noon.AddHours(-2))], Cancel);
        samples.Samples.Add(Sample(Noon.AddMinutes(-110)));
        samples.Samples.Add(Sample(Noon.AddMinutes(-100)));
        QualityPeriod period = QualityPeriod.Of(Noon.AddMinutes(-105), Noon.AddMinutes(-95), Noon)!;

        IReadOnlyList<QualitySignalWindow> windows = await new QualitySignalReader(rollups, samples)
            .WindowsAsync(period, Cancel);

        Assert.Equal([Noon.AddMinutes(-100)], windows.Select(window => window.Start));
    }

    [Fact]
    public async Task TheFiguresOfAPeriodAreItsWindowsFolded()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();
        await rollups.SaveAsync([Rollup(Noon.AddHours(-2))], Cancel);
        samples.Samples.Add(Sample(Noon.AddMinutes(-30)));

        SignalFigures figures = Assert.Single(await new QualitySignalReader(rollups, samples).FiguresAsync(Day, Cancel));

        Assert.Equal(361, figures.Samples);
        Assert.Equal(22_000, figures.CarrierToNoiseLowest);
    }

    private static QualitySignalRollup Rollup(DateTime start)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            start,
            new TunerDeviceId("adapter0.frontend0"),
            new NetworkId(4),
            new ServiceId(101),
            360,
            360,
            0,
            0,
            24_000,
            24_000,
            24_000,
            []);

    private static QualitySignalSample Sample(DateTime at)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter0.frontend0"),
            new NetworkId(4),
            new ServiceId(101),
            SignalSample.WithLock(at, 22_000, at));
}
