using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class CollectionBoostTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AWalkingBoostNamesItselfWhenAnotherIsAskedFor()
    {
        var held = new HoldingStreams([Stream()]);
        await using ServiceProvider provider = Provider(held);
        using var boost = new CollectionBoost(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CollectionSettings(),
            TimeProvider.System,
            NullLogger<CollectionBoost>.Instance);

        Task<BoostStarted?> starting = boost.StartAsync(_ => true, Cancel);

        await held.Listed;

        BoostVerdict verdict = boost.MayStart();

        Assert.Equal(BoostRefusal.OneIsAlreadyRunning, verdict.Refusal);
        Assert.NotNull(verdict.RunningId);
        Assert.Null(await boost.StartAsync(_ => true, Cancel));

        held.Release();
        await starting;
    }

    [Fact]
    public async Task ABoostThatMatchesNothingIsNotStartedAndLeavesTheGuardClosedOnlyByCooldown()
    {
        var held = new HoldingStreams([Stream()]);
        await using ServiceProvider provider = Provider(held);
        using var boost = new CollectionBoost(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CollectionSettings(),
            TimeProvider.System,
            NullLogger<CollectionBoost>.Instance);

        held.Release();

        BoostStarted? started = await boost.StartAsync(_ => false, Cancel);

        Assert.NotNull(started);
        Assert.Equal(0, started.Streams);
        Assert.Equal(BoostRefusal.TooSoonAfterTheLastOne, boost.MayStart().Refusal);
    }

    private static ServiceProvider Provider(HoldingStreams held)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScoped<IBroadcastStreamDirectory>(_ => held);

        return services.BuildServiceProvider();
    }

    private static BroadcastStream Stream()
        => new(
            new NetworkId(4),
            new TransportStreamId(32_736),
            TuningParameters.Terrestrial(22),
            [new ServiceId(1049)]);
}

internal sealed class HoldingStreams(IReadOnlyList<BroadcastStream> streams) : IBroadcastStreamDirectory
{
    private readonly SemaphoreSlim listed = new(0);
    private readonly SemaphoreSlim allowed = new(0);

    public Task Listed => listed.WaitAsync(TimeSpan.FromSeconds(20));

    public void Release() => allowed.Release(1_000);

    public async Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
    {
        listed.Release();

        await allowed.WaitAsync(cancellationToken);

        return streams;
    }

    public Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IntendedStream>>([]);
}
