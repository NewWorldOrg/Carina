using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Channels;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SatelliteTransportStreamRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task TheSatelliteReferenceRowsAreReplacedOneSlotAtATime()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        ISatelliteTransportStreamRepository streams = Streams(scope);

        IReadOnlyList<SatelliteTransportStream> seeded = await streams.ListAsync(Cancel);
        Assert.Equal(10, seeded.Count);

        await streams.ReplaceSlotAsync(
            15,
            [
                SatelliteTransportStream.Rehydrate(15, 0, new TransportStreamId(0x40F0)),
                SatelliteTransportStream.Rehydrate(15, 1, new TransportStreamId(0x40F1)),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<SatelliteTransportStream> slot = await Streams(reading)
            .ListForSlotAsync(15, Cancel);

        Assert.Equal(2, slot.Count);
        Assert.Equal(11, (await Streams(reading).ListAsync(Cancel)).Count);
    }

    [Fact]
    public async Task ASatelliteSlotReplacedInsideALargerWriteGoesBackWithIt()
    {
        const int Slot = 21;

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        ISatelliteTransportStreamRepository streams = Streams(scope);
        IReadOnlyList<SatelliteTransportStream> before = await streams.ListForSlotAsync(Slot, Cancel);

        await Assert.ThrowsAsync<StoreRefusedException>(
            () => scope.ServiceProvider.GetRequiredService<IAtomicWrite>().AllOrNothingAsync<int>(
                async token =>
                {
                    await streams.ReplaceSlotAsync(
                        Slot,
                        [SatelliteTransportStream.Rehydrate(Slot, 0, new TransportStreamId(0x40F0))],
                        token);

                    throw new StoreRefusedException("whatever ends the write after the slot");
                },
                Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<SatelliteTransportStream> after = await Streams(reading).ListForSlotAsync(Slot, Cancel);

        Assert.Equal(
            before.Select(stream => stream.TransportStreamId),
            after.Select(stream => stream.TransportStreamId));
    }

    private static ISatelliteTransportStreamRepository Streams(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ISatelliteTransportStreamRepository>();
}
