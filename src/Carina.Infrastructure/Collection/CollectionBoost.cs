using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record BoostStarted(Guid BoostId, int Streams);

public sealed class CollectionBoost(
    IServiceScopeFactory scopes,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<CollectionBoost> logger) : IDisposable
{
    private readonly Lock gate = new();

    private Guid? running;
    private DateTime? lastFinishedAt;
    private CancellationTokenSource? walking;
    private Task? walk;

    public BoostVerdict MayStart()
    {
        lock (gate)
        {
            return BoostGuard.Of(
                running,
                lastFinishedAt,
                clock.GetUtcNow().UtcDateTime,
                settings.BetweenBoosts);
        }
    }

    public async Task<BoostStarted?> StartAsync(
        Func<BroadcastStream, bool> wanted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var boostId = Guid.NewGuid();

        lock (gate)
        {
            if (!MayStartUnderGate())
            {
                return null;
            }

            running = boostId;
        }

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IReadOnlyList<BroadcastStream> offered = await scope.ServiceProvider
                .GetRequiredService<IBroadcastStreamDirectory>()
                .ListAsync(cancellationToken);
            BroadcastStream[] asked = [.. offered.Where(wanted)];

            if (asked.Length == 0)
            {
                Finish();

                return new BoostStarted(boostId, 0);
            }

            var deadline = new CancellationTokenSource(settings.LongestBoost);

            walking = deadline;
            walk = Task.Run(() => WalkAsync(boostId, asked, deadline), CancellationToken.None);

            return new BoostStarted(boostId, asked.Length);
        }
        catch (Exception)
        {
            Finish();

            throw;
        }
    }

    public Task Settled => walk ?? Task.CompletedTask;

    public void Dispose()
    {
        walking?.Dispose();
        walking = null;
    }

    private async Task WalkAsync(Guid boostId, IReadOnlyList<BroadcastStream> asked, CancellationTokenSource deadline)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            RoundResult walked = await scope.ServiceProvider
                .GetRequiredService<CollectionRound>()
                .WalkAsync(asked, CancellationToken.None, deadline.Token);

            logger.LogInformation(
                "Boost {BoostId} visited {Visited} of {Asked} stream(s).",
                boostId,
                walked.Visited,
                asked.Count);
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Boost {BoostId} ended early.", boostId);
        }
        finally
        {
            deadline.Dispose();
            Finish();
        }
    }

    private bool MayStartUnderGate()
        => BoostGuard.Of(
            running,
            lastFinishedAt,
            clock.GetUtcNow().UtcDateTime,
            settings.BetweenBoosts).IsAllowed;

    private void Finish()
    {
        lock (gate)
        {
            running = null;
            lastFinishedAt = clock.GetUtcNow().UtcDateTime;
        }
    }
}
