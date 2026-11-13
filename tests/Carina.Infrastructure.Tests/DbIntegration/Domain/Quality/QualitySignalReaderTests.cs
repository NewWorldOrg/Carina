using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualitySignalReaderTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime DayOne = new(2026, 9, 6, 19, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "what the reader answers with is the windows first and the samples they have not reached")]
    public async Task WhatTheReaderAnswersWithIsTheWindowsFirstAndTheSamplesTheyHaveNotReached()
    {
        await ClearAsync();

        await SaveAsync([Rollup(Noon.AddHours(-2), samples: 360, locked: 360)]);
        await AddAsync(
        [
            Sample(Noon.AddHours(-3), SignalSample.WithoutLock(Noon.AddHours(-3)), "live-0"),
            Sample(Noon.AddMinutes(-30), SignalSample.WithLock(Noon.AddMinutes(-30), 12000, Noon.AddMinutes(-30)), "live-2"),
        ]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<SignalFigures> figures = await Reader(reading)
            .FiguresAsync(QualityPeriod.Of(Noon.AddDays(-1), Noon, Noon)!, Cancel);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(361, figure.Samples);
        Assert.Equal(12000, figure.CarrierToNoiseLowest);
    }

    [Fact(DisplayName = "the reader takes raw samples only past the last hour rolled up, so none is counted twice")]
    public async Task TheReaderTakesRawSamplesOnlyPastTheLastHourRolledUp()
    {
        DateTime rolledThrough = DayOne.AddDays(1);
        QualityTrendFrame frame = QualityTrendFrame.Over(1, rolledThrough.AddMinutes(30), QualityTrendStep.Hour)!;

        await ClearAsync();
        await SaveAsync([.. Enumerable.Range(0, 24).Select(hour => Hourly(frame.Period.From.AddHours(hour)))]);
        await AddAsync(
        [
            Locked(rolledThrough.AddMinutes(-30)),
            Locked(rolledThrough.AddMinutes(5)),
            Locked(rolledThrough.AddMinutes(15)),
        ]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<QualitySignalWindow> windows = await Reader(reading).WindowsAsync(frame, Cancel);

        Assert.Equal(24, windows.Count(window => window.Samples == 6));
        Assert.Equal(
            [rolledThrough.AddMinutes(5), rolledThrough.AddMinutes(15)],
            windows.Where(window => window.Samples == 1).Select(window => window.Start));
    }

    private static IQualitySignalReader Reader(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualitySignalReader>();

    private static QualitySignalSample Sample(DateTime at, SignalSample signal, string session)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            signal);

    private static QualitySignalRollup Rollup(DateTime windowStart, long samples, long locked)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            windowStart,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            samples,
            locked,
            0,
            0,
            30000,
            30000,
            30000,
            [new LayerErrorRate(0, 0, 0)]);

    private static QualitySignalSample Locked(DateTime at)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter0"),
            new NetworkId(1),
            new ServiceId(101),
            SignalSample.WithLock(at, 30_000, at));

    private static QualitySignalRollup Hourly(DateTime windowStart)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            windowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(1),
            new ServiceId(101),
            6,
            6,
            0,
            0,
            30_000,
            30_000,
            30_000,
            [new LayerErrorRate(0, 0, 0)]);

    private async Task AddAsync(IReadOnlyList<QualitySignalSample> samples)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await writing.ServiceProvider.GetRequiredService<IQualitySignalSampleRepository>().AddAsync(samples, Cancel);
    }

    private async Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await writing.ServiceProvider.GetRequiredService<IQualitySignalRollupRepository>().SaveAsync(rollups, Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        CarinaDbContext context = clearing.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Set<QualitySignalSample>().ExecuteDeleteAsync(Cancel);
        await context.Set<QualitySignalRollup>().ExecuteDeleteAsync(Cancel);
    }
}
