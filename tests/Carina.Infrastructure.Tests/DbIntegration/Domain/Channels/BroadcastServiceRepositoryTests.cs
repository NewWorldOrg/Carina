using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Channels;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class BroadcastServiceRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task AServiceComesBackWithTheIdentifiersItWasStoredUnder()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        await services.AddAsync(Service(network, 1), Cancel);

        BroadcastService? found = await services.FindAsync(new NetworkId(network), new ServiceId(1), Cancel);

        Assert.NotNull(found);
        Assert.Equal("Fixture Service", found.Name);
        Assert.Equal(ServiceCategory.Television, found.Category);
        Assert.Equal(At, found.DiscoveredAt);
    }

    [Fact]
    public async Task DeletingAServiceTakesItsCandidatesAndNothingElse()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        ICandidateChannelRepository candidates = Candidates(scope);
        await services.AddAsync(Service(network, 1), Cancel);
        await services.AddAsync(Service(network, 2), Cancel);
        CandidateChannel doomed = Candidate(network, 1, 27);
        CandidateChannel spared = Candidate(network, 2, 27);
        await candidates.AddAsync(doomed, Cancel);
        await candidates.AddAsync(spared, Cancel);

        await services.RemoveAsync(new NetworkId(network), new ServiceId(1), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository repository = Candidates(reading);
        Assert.Null(await repository.FindAsync(doomed.Id, Cancel));
        Assert.NotNull(await repository.FindAsync(spared.Id, Cancel));
    }

    [Fact]
    public async Task DeletingAChannelDefinitionLeavesTheProgrammesCollectedThroughItInPlace()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        ICandidateChannelRepository candidates = Candidates(scope);
        await services.AddAsync(Service(network, 1), Cancel);
        CandidateChannel doomed = Candidate(network, 1, 27);
        await candidates.AddAsync(doomed, Cancel);
        await Programmes(scope).AddAsync(
            Programme.Discover(Collected(network, 1, 40_001), At),
            Cancel);

        await candidates.RemoveAsync(doomed.Id, Cancel);
        await services.RemoveAsync(new NetworkId(network), new ServiceId(1), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.NotNull(await Programmes(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1), new EventId(40_001)),
            Cancel));
    }

    [Fact]
    public async Task AWriteInsideAnotherOneIsRefusedBeforeItWritesAnything()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        IAtomicWrite writes = scope.ServiceProvider.GetRequiredService<IAtomicWrite>();

        await writes.AllOrNothingAsync<int>(
            async outer =>
            {
                await services.AddAsync(Service(network, 1), outer);

                await Assert.ThrowsAsync<NestedWriteRefusedException>(
                    () => writes.AllOrNothingAsync<int>(
                        async inner =>
                        {
                            await services.AddAsync(Service(network, 2), inner);

                            return 0;
                        },
                        outer));

                return 0;
            },
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IBroadcastServiceRepository stored = Services(reading);

        Assert.NotNull(await stored.FindAsync(new NetworkId(network), new ServiceId(1), Cancel));
        Assert.Null(await stored.FindAsync(new NetworkId(network), new ServiceId(2), Cancel));
    }

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    private static BroadcastService Service(int network, int service)
        => BroadcastService.Discover(
            new NetworkId(network),
            new ServiceId(service),
            "Fixture Service",
            ServiceCategory.Television,
            At);

    private static CandidateChannel Candidate(int network, int service, int physicalChannel)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(network),
            new ServiceId(service),
            TuningParameters.Terrestrial(physicalChannel),
            At);

    private static ProgrammeBroadcast Collected(int network, int service, int carried)
        => new(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(32739),
            At.AddHours(22),
            At.AddHours(23),
            "Fixture Programme",
            "Fixture description",
            IsShadow: false);

    private static IBroadcastServiceRepository Services(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IBroadcastServiceRepository>();

    private static ICandidateChannelRepository Candidates(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ICandidateChannelRepository>();

    private static IProgrammeRepository Programmes(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
}
