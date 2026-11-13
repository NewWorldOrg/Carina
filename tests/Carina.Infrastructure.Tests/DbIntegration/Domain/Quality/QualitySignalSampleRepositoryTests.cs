using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualitySignalSampleRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a sample comes back with every time and every layer it went in with")]
    public async Task ASampleComesBackWithEveryTimeAndEveryLayerItWentInWith()
    {
        await ClearAsync();

        await AddAsync(
        [
            Sample(
                Noon,
                SignalSample.WithLock(
                    Noon,
                    30000,
                    Noon.AddSeconds(-1),
                    [new LayerBitErrorCounts(0, 0, 1600000), new LayerBitErrorCounts(1, 2, 64000000)],
                    Noon.AddSeconds(-1))),
        ]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        QualitySignalSample held = Assert.Single(
            await Samples(reading).ListTakenBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.Equal(Noon, held.Signal.LockReadAt);
        Assert.Equal(Noon.AddSeconds(-1), held.Signal.CarrierToNoiseReadAt);
        Assert.Equal(30000, held.Signal.CarrierToNoiseMilliDecibels);
        Assert.Equal([0, 1], held.Signal.BitErrors.Select(counts => counts.Layer));
        Assert.True(held.Signal.WasTaken);
    }

    [Fact(DisplayName = "a reading that could not be taken comes back saying which way it could not be")]
    public async Task AReadingThatCouldNotBeTakenComesBackSayingWhichWayItCouldNotBe()
    {
        await ClearAsync();

        await AddAsync([Sample(Noon, SignalSample.NotTaken(Noon, SignalNotTaken.NoTimeGiven))]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        QualitySignalSample held = Assert.Single(
            await Samples(reading).ListTakenBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.False(held.Signal.WasTaken);
        Assert.Equal(SignalNotTaken.NoTimeGiven, held.Signal.NotTakenBecause);
    }

    [Fact(DisplayName = "a sample comes back on the physical channel it was taken on, and one kept without a channel comes back without one")]
    public async Task ASampleComesBackOnThePhysicalChannelItWasTakenOn()
    {
        await ClearAsync();

        await AddAsync(
        [
            Sample(Noon, SignalSample.WithoutLock(Noon), "live-1", channel: 27),
            Sample(Noon.AddSeconds(10), SignalSample.WithoutLock(Noon.AddSeconds(10)), "live-2"),
        ]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<QualitySignalSample> held = await Samples(reading)
            .ListTakenBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel);

        Assert.Equal([27, null], held.OrderBy(sample => sample.TakenAt).Select(sample => sample.PhysicalChannel));
    }

    [Fact(DisplayName = "a sweep lets go only of the samples taken before its cutoff")]
    public async Task ASweepLetsGoOnlyOfTheSamplesTakenBeforeItsCutoff()
    {
        await ClearAsync();

        await AddAsync(
        [
            Sample(Noon.AddDays(-8), SignalSample.WithoutLock(Noon.AddDays(-8)), "live-1"),
            Sample(Noon, SignalSample.WithoutLock(Noon), "live-2"),
        ]);

        await using AsyncServiceScope sweeping = container.CreateAsyncScope();

        IQualitySignalSampleRepository repository = Samples(sweeping);

        Assert.Equal(1, await repository.ForgetTakenBeforeAsync(Noon.AddDays(-7), Cancel));
        Assert.Single(await repository.ListTakenBetweenAsync(Noon.AddDays(-30), Noon.AddDays(1), Cancel));
    }

    private static IQualitySignalSampleRepository Samples(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualitySignalSampleRepository>();

    private static QualitySignalSample Sample(
        DateTime at,
        SignalSample signal,
        string session = "live-1",
        int? channel = null)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            signal,
            channel);

    private async Task AddAsync(IReadOnlyList<QualitySignalSample> samples)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await Samples(writing).AddAsync(samples, Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualitySignalSample>()
            .ExecuteDeleteAsync(Cancel);
    }
}
