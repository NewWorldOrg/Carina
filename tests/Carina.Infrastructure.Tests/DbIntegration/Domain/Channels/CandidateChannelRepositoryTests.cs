using System.Data.Common;

using Carina.Domain.Channels;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Channels;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CandidateChannelRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task SelectingACandidateLeavesExactlyOneSelectedForTheService()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        ICandidateChannelRepository candidates = Candidates(scope);
        await services.AddAsync(Service(network, 1), Cancel);
        CandidateChannel first = Candidate(network, 1, 27);
        CandidateChannel second = Candidate(network, 1, 28);
        await candidates.AddAsync(first, Cancel);
        await candidates.AddAsync(second, Cancel);

        await candidates.SelectAsync(
            first.Id, SelectionSource.Manual, SignalMeasurement.WithLock(At, 21_000), At, Cancel);
        await candidates.SelectAsync(
            second.Id, SelectionSource.AutoSwitch, SignalMeasurement.WithLock(At, 22_000), At, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<CandidateChannel> stored = await Candidates(reading)
            .ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel);

        CandidateChannel selected = Assert.Single(stored, candidate => candidate.IsSelected);
        Assert.Equal(second.Id, selected.Id);
        Assert.Equal(SelectionSource.AutoSwitch, selected.SelectionSource);
        Assert.Equal(22_000, selected.SelectionMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public async Task AScoreComesBackWithEveryFigureItWasWrittenWith()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        CandidateChannel candidate = Candidate(network, 1, 27);
        await Candidates(scope).AddAsync(candidate, Cancel);
        CandidateScore score = CandidateScore.Of(400, 300, 21_500, 0.0002, At, At.AddDays(7), At.AddDays(7));

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        bool written = await Candidates(writing).ScoreAsync(candidate.Id, score, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CandidateChannel? stored = await Candidates(reading).FindAsync(candidate.Id, Cancel);

        Assert.True(written);
        Assert.Equal(score, stored?.Score);
    }

    [Fact]
    public async Task ACandidateNeverScoredComesBackWithNoScore()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        CandidateChannel candidate = Candidate(network, 1, 27);
        await Candidates(scope).AddAsync(candidate, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CandidateChannel? stored = await Candidates(reading).FindAsync(candidate.Id, Cancel);

        Assert.Null(stored?.Score);
    }

    [Fact(DisplayName = "writing a score back leaves a selection made meanwhile as it was made")]
    public async Task WritingAScoreBackLeavesASelectionMadeMeanwhileAsItWasMade()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        CandidateChannel first = Candidate(network, 1, 27);
        CandidateChannel second = Candidate(network, 1, 28);
        ICandidateChannelRepository candidates = Candidates(scope);
        await candidates.AddAsync(first, Cancel);
        await candidates.AddAsync(second, Cancel);
        await candidates.SelectAsync(first.Id, SelectionSource.Manual, null, At, Cancel);

        await using AsyncServiceScope scoring = container.CreateAsyncScope();
        ICandidateChannelRepository scorer = Candidates(scoring);
        Assert.NotNull(await scorer.FindAsync(first.Id, Cancel));

        await using AsyncServiceScope choosing = container.CreateAsyncScope();
        await Candidates(choosing)
            .SelectAsync(second.Id, SelectionSource.Manual, null, At.AddHours(1), Cancel);

        await scorer.ScoreAsync(
            first.Id,
            CandidateScore.Of(360, 360, 24_000, 0, At, At.AddDays(7), At.AddDays(7)),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<CandidateChannel> stored = await Candidates(reading)
            .ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel);

        CandidateChannel selected = Assert.Single(stored, candidate => candidate.IsSelected);
        Assert.Equal(second.Id, selected.Id);
        Assert.Equal(At.AddHours(1), selected.SelectedAt);
        CandidateChannel scored = Assert.Single(stored, candidate => candidate.Id.Equals(first.Id));
        Assert.Null(scored.SelectionSource);
        Assert.NotNull(scored.Score);
    }

    [Fact]
    public async Task ScoringACandidateThatIsGoneWritesNothing()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        bool written = await Candidates(scope).ScoreAsync(
            CandidateChannelId.New(),
            CandidateScore.Of(360, 360, 24_000, 0, At, At.AddDays(7), At.AddDays(7)),
            Cancel);

        Assert.False(written);
    }

    [Fact(DisplayName = "a candidate removed between being read and being written is one that was not scored, and the next one in the same context is still written")]
    public async Task ACandidateRemovedBetweenBeingReadAndBeingWrittenIsNotScoredAndTheNextStillIs()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        CandidateChannel gone = Candidate(network, 1, 27);
        CandidateChannel staying = Candidate(network, 1, 28);
        ICandidateChannelRepository candidates = Candidates(scope);
        await candidates.AddAsync(gone, Cancel);
        await candidates.AddAsync(staying, Cancel);
        CandidateScore score = CandidateScore.Of(360, 360, 24_000, 0, At, At.AddDays(7), At.AddDays(7));

        await using ServiceProvider watched = database.Infrastructure(services => services.ConfigureDbContext<CarinaDbContext>(
            options => options.AddInterceptors(new RemovedBeforeItIsWritten(container, gone.Id))));
        await using AsyncServiceScope scoring = watched.CreateAsyncScope();
        ICandidateChannelRepository scorer = Candidates(scoring);

        bool wroteTheOneThatWent = await scorer.ScoreAsync(gone.Id, score, Cancel);
        bool wroteTheOneThatStayed = await scorer.ScoreAsync(staying.Id, score, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<CandidateChannel> stored = await Candidates(reading)
            .ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel);

        Assert.False(wroteTheOneThatWent);
        Assert.True(wroteTheOneThatStayed);
        Assert.Equal(score, Assert.Single(stored).Score);
    }

    [Fact]
    public async Task ClearingTheSelectionLeavesTheServiceWithNoWayToTuneItAndNoRepair()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        ICandidateChannelRepository candidates = Candidates(scope);
        CandidateChannel only = Candidate(network, 1, 27);
        await candidates.AddAsync(only, Cancel);
        await candidates.SelectAsync(
            only.Id, SelectionSource.Manual, SignalMeasurement.WithLock(At, 21_000), At, Cancel);

        await candidates.ClearSelectionAsync(new NetworkId(network), new ServiceId(1), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository repository = Candidates(reading);
        Assert.Null(await repository.FindSelectedAsync(new NetworkId(network), new ServiceId(1), Cancel));
        CandidateChannel left = Assert.Single(
            await repository.ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel));

        Assert.Null(left.SelectionMeasurement);
        Assert.Null(left.LastMeasurement);
    }

    [Fact]
    public async Task ACandidateOutOfRotationIsLeftOutOfTheRoundAndStaysVisible()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        ICandidateChannelRepository candidates = Candidates(scope);
        CandidateChannel backingOff = Candidate(network, 1, 27);
        CandidateChannel lost = Candidate(network, 1, 28);
        await candidates.AddAsync(backingOff, Cancel);
        await candidates.AddAsync(lost, Cancel);

        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        backingOff.RecordTuningFailure(backoff, At);
        lost.RecordTuningFailure(backoff, At);
        lost.RecordTuningFailure(backoff, At);
        await candidates.SaveAsync(backingOff, Cancel);
        await candidates.SaveAsync(lost, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository repository = Candidates(reading);
        IReadOnlyList<CandidateChannel> inRotation = await repository.ListInRotationAsync(At.AddMinutes(2), Cancel);
        IReadOnlyList<CandidateChannel> needingAttention = await repository.ListNeedingAttentionAsync(Cancel);

        Assert.Contains(inRotation, candidate => candidate.Id.Equals(backingOff.Id));
        Assert.DoesNotContain(inRotation, candidate => candidate.Id.Equals(lost.Id));
        Assert.Contains(needingAttention, candidate => candidate.Id.Equals(lost.Id));
        Assert.Equal(At, needingAttention.Single(candidate => candidate.Id.Equals(lost.Id)).NeedsAttentionSince);
    }

    [Fact]
    public async Task ACandidateBackingOffIsSkippedUntilItsNextAttemptIsDue()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        ICandidateChannelRepository candidates = Candidates(scope);
        CandidateChannel candidate = Candidate(network, 1, 27);
        await candidates.AddAsync(candidate, Cancel);
        candidate.RecordTuningFailure(RotationBackoff.Default, At);
        await candidates.SaveAsync(candidate, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository repository = Candidates(reading);

        Assert.DoesNotContain(
            await repository.ListInRotationAsync(At, Cancel),
            stored => stored.Id.Equals(candidate.Id));
        Assert.Contains(
            await repository.ListInRotationAsync(At.AddHours(2), Cancel),
            stored => stored.Id.Equals(candidate.Id));
    }

    [Fact]
    public async Task RecordingAFailureDoesNotUndoASelectionMadeMeanwhile()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        CandidateChannel candidate = Candidate(network, 1, 27);
        await Candidates(scope).AddAsync(candidate, Cancel);

        await using AsyncServiceScope elsewhere = container.CreateAsyncScope();
        await Candidates(elsewhere)
            .SelectAsync(candidate.Id, SelectionSource.AutoSwitch, null, At, Cancel);

        candidate.RecordTuningFailure(RotationBackoff.Default, At.AddMinutes(1));
        await using AsyncServiceScope stale = container.CreateAsyncScope();
        await Candidates(stale).SaveAsync(candidate, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CandidateChannel? stored = await Candidates(reading).FindAsync(candidate.Id, Cancel);
        Assert.True(stored!.IsSelected);
        Assert.Equal(SelectionSource.AutoSwitch, stored.SelectionSource);
        Assert.Equal(RotationState.BackingOff, stored.RotationState);
    }

    [Fact]
    public async Task AChangedTunerLedgerMarksEveryCandidateForRevalidation()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Service(network, 1), Cancel);
        ICandidateChannelRepository candidates = Candidates(scope);
        CandidateChannel candidate = Candidate(network, 1, 27);
        await candidates.AddAsync(candidate, Cancel);

        await candidates.RequireRevalidationAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CandidateChannel? stored = await Candidates(reading).FindAsync(candidate.Id, Cancel);
        Assert.True(stored!.NeedsRevalidation);
    }

    [Fact]
    public async Task ServicesSharingOneChannelEachKeepACandidateOfTheirOwn()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        ICandidateChannelRepository candidates = Candidates(scope);
        await services.AddAsync(Service(network, 1), Cancel);
        await services.AddAsync(Service(network, 2), Cancel);

        var carrying = TuningParameters.Terrestrial(27);
        var measured = SignalMeasurement.WithLock(At, 21_000);

        foreach (int service in (int[])[1, 2])
        {
            var candidate = CandidateChannel.Discover(
                CandidateChannelId.New(),
                new NetworkId(network),
                new ServiceId(service),
                carrying,
                At);
            candidate.RecordTuningSuccess(measured, At);

            await candidates.AddAsync(candidate, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository repository = Candidates(reading);

        foreach (int service in (int[])[1, 2])
        {
            CandidateChannel stored = Assert.Single(
                await repository.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));

            Assert.Equal(27, stored.Tuning.PhysicalChannel);
            Assert.Equal(21_000, stored.LastMeasurement?.CnrMilliDecibels);
        }
    }

    [Fact]
    public async Task TheChannelsOfAServiceComeBackWithTheBestMeasuredAheadOfTheOneFoundFirst()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IBroadcastServiceRepository services = Services(scope);
        ICandidateChannelRepository candidates = Candidates(scope);
        await services.AddAsync(Service(network, 1), Cancel);

        CandidateChannel weak = Candidate(network, 1, 41);
        CandidateChannel strong = Candidate(network, 1, 58);
        CandidateChannel unlocked = Candidate(network, 1, 44);

        weak.RecordTuningSuccess(SignalMeasurement.WithLock(At, 12_000), At);
        strong.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1), 29_000), At.AddHours(1));
        unlocked.RecordTuningSuccess(SignalMeasurement.WithoutLock(At.AddHours(2)), At.AddHours(2));

        await candidates.AddAsync(weak, Cancel);
        await candidates.AddAsync(strong, Cancel);
        await candidates.AddAsync(unlocked, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<CandidateChannel> stored = await Candidates(reading)
            .ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel);

        Assert.Equal([58, 41, 44], stored.Select(candidate => candidate.Tuning.PhysicalChannel));
    }

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    /// <summary>
    /// Removes one candidate, through a connection of its own, just before the first update the
    /// context it watches sends.
    /// </summary>
    private sealed class RemovedBeforeItIsWritten(ServiceProvider container, CandidateChannelId removed)
        : DbCommandInterceptor
    {
        private bool done;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!done && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                done = true;

                await using AsyncServiceScope removing = container.CreateAsyncScope();
                await Candidates(removing).RemoveAsync(removed, cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

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

    private static ICandidateChannelRepository Candidates(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ICandidateChannelRepository>();

    private static IBroadcastServiceRepository Services(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IBroadcastServiceRepository>();
}
