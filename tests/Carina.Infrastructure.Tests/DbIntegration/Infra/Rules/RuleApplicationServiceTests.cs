using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Rules;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RuleApplicationServiceTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ahead = Now.AddDays(20);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 3049;

    private static readonly DateTime AheadForRetirement = Now.AddDays(24);

    private const int ListedForRetirement = 3149;

    private readonly List<ServiceProvider> standingIn = [];

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (AsyncServiceScope clearing = container.CreateAsyncScope())
        {
            await Context(clearing).Set<Reservation>().Where(reservation => reservation.RuleId != null).ExecuteDeleteAsync(Cancel);
            await Context(clearing).Set<Rule>().ExecuteDeleteAsync(Cancel);
        }

        await container.DisposeAsync();

        foreach (ServiceProvider standing in standingIn)
        {
            await standing.DisposeAsync();
        }
    }

    [Fact]
    public async Task AReservationARuleMadeIsInTheLedgerUnderTheRuleThatMadeIt()
    {
        Rule rule = Written(0x51, "keyword=upland");
        Programme taken = await SownAsync(9101, "an upland walk");
        Programme left = await SownAsync(9102, "a lowland drive");
        await VisitedAsync(VisitOutcome.Complete);
        await AddAsync(rule);

        await using (AsyncServiceScope scope = StandingIn())
        {
            RuleApplicationRun run = await Applying(scope).EverythingAsync(Cancel);

            Assert.Contains(run.Made, made => made.SnapshotName == "an upland walk");
        }

        Reservation? found = await FindAsync(taken);

        Assert.NotNull(found);
        Assert.Equal(rule.Id, found.RuleId);
        Assert.Equal(ReservationState.Scheduled, found.State);
        Assert.Equal("an upland walk", found.SnapshotName);
        Assert.Equal(taken.StartsAt, found.ProgrammeStartsAt);
        Assert.Null(await FindAsync(left));
    }

    [Fact]
    public async Task AReservationWhoseProgrammeVanishedIsGoneFromTheLedgerAfterASweep()
    {
        Rule rule = Written(0x52, "keyword=moorland");
        Programme vanished = Sown(9201, "a moorland walk");
        Reservation standing = Standing(vanished, rule.Id);
        await VisitedAsync(VisitOutcome.Complete);
        await AddAsync(rule);
        await AddAsync(standing);

        Assert.NotNull(await FindAsync(vanished));

        await using (AsyncServiceScope scope = StandingIn())
        {
            RuleApplicationRun run = await Applying(scope).EverythingAsync(Cancel);

            Assert.Contains(run.Withdrawn, gone => gone.Id.Equals(standing.Id));
        }

        Assert.Null(await FindAsync(vanished));
    }

    [Fact]
    public async Task AReservationTheStreamCouldNotBeCollectedForIsStillInTheLedgerAfterASweep()
    {
        Rule rule = Written(0x53, "keyword=marshland");
        Programme kept = Sown(9301, "a marshland walk");
        Reservation standing = Standing(kept, rule.Id);
        await VisitedAsync(VisitOutcome.Incomplete);
        await AddAsync(rule);
        await AddAsync(standing);

        await using (AsyncServiceScope scope = StandingIn())
        {
            RuleApplicationRun run = await Applying(scope).EverythingAsync(Cancel);

            Assert.DoesNotContain(run.Withdrawn, gone => gone.Id.Equals(standing.Id));
        }

        Assert.NotNull(await FindAsync(kept));
    }

    [Fact]
    public async Task DroppingARuleTakesItsReservationsOutOfTheLedgerBeforeTheRuleItselfGoes()
    {
        Rule rule = Written(0x54, "keyword=heathland");
        Programme going = Sown(9401, "a heathland walk");
        Reservation standing = Standing(going, rule.Id);
        await VisitedAsync(VisitOutcome.Complete);
        await AddAsync(rule);
        await AddAsync(standing);

        await using (AsyncServiceScope scope = StandingIn())
        {
            IReadOnlyList<Reservation> dropped = await Applying(scope).DroppedAsync(rule.Id, Cancel);

            Assert.Contains(dropped, gone => gone.Id.Equals(standing.Id));
        }

        Assert.Null(await FindAsync(going));

        await using AsyncServiceScope removing = container.CreateAsyncScope();
        IRuleRepository repository = Rules(removing);
        await repository.RemoveAsync((await repository.FindAsync(rule.Id, Cancel))!, Cancel);

        Assert.Null(await repository.FindAsync(rule.Id, Cancel));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    public async Task AProgrammeWhoseStartMovedKeepsTheOneReservationTheRuleMadeForIt(int minutes)
    {
        int carried = 9500 + minutes;
        Rule rule = Written(0x55 + minutes, $"keyword=woodland{minutes}");
        Reservation standing = Standing(Sown(carried, $"a woodland{minutes} walk"), rule.Id);
        await SownAsync(carried, $"a woodland{minutes} walk", Ahead.AddMinutes(minutes));
        await VisitedAsync(VisitOutcome.Complete);
        await AddAsync(rule);
        await AddAsync(standing);

        await using (AsyncServiceScope scope = StandingIn())
        {
            RuleApplicationRun run = await Applying(scope).EverythingAsync(Cancel);

            Assert.Empty(run.Made);
            Assert.DoesNotContain(run.Withdrawn, gone => gone.Id.Equals(standing.Id));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EventId eventId = new(carried);
        List<ReservationId> held = await Context(reading).Set<Reservation>()
            .Where(reservation => reservation.EventId == eventId)
            .Select(reservation => reservation.Id)
            .ToListAsync(Cancel);

        Assert.Equal([standing.Id], held);
    }

    [Fact]
    public async Task RetiringARuleLeavesNoStandingReservationBehindThatNothingOwnsAnyMore()
    {
        Rule going = WrittenForRetirement(0x71);
        Rule staying = WrittenForRetirement(0x72);
        await AddAsync(going);
        await AddAsync(staying);

        Reservation standing = await StandingAsync(9201, going.Id);
        Reservation recording = await ClaimedAsync(await StandingAsync(9202, going.Id));
        Reservation cancelled = await StandingAsync(9203, going.Id, ReservationState.Cancelled);
        Reservation another = await StandingAsync(9204, staying.Id);

        await using (AsyncServiceScope scope = StandingInForRetirement())
        {
            Assert.NotNull(await Applying(scope).RetiredAsync(going.Id, Cancel));
        }

        Assert.Null(await FindAsyncForRetirement(standing.Id));
        Assert.NotNull(await FindAsyncForRetirement(recording.Id));
        Assert.NotNull(await FindAsyncForRetirement(cancelled.Id));
        Assert.Equal(staying.Id, (await FindAsyncForRetirement(another.Id))!.RuleId);
        Assert.Empty(await OrphanedAndStandingAmongAsync(standing, recording, cancelled, another));
        Assert.Null(await ReadRuleAsync(going.Id));
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    [Fact]
    public async Task WhatIsKeptWhenTheRuleGoesIsLetGoOfItBeforeTheRuleIsTakenAway()
    {
        Rule going = WrittenForRetirement(0x78);
        await AddAsync(going);
        Reservation recording = await ClaimedAsync(await StandingAsync(9211, going.Id));
        Reservation cancelled = await StandingAsync(9212, going.Id, ReservationState.Cancelled);

        await using (AsyncServiceScope scope = StandingInForRetirement())
        {
            Assert.NotNull(await Applying(scope).RetiredAsync(going.Id, Cancel));
        }

        Reservation? kept = await FindAsyncForRetirement(recording.Id);
        Reservation? held = await FindAsyncForRetirement(cancelled.Id);

        Assert.NotNull(kept);
        Assert.Null(kept.RuleId);
        Assert.True(kept.IsPinned);
        Assert.NotNull(held);
        Assert.Null(held.RuleId);
        Assert.Equal(ReservationState.Cancelled, held.State);
        Assert.Null(await ReadRuleAsync(going.Id));
    }

    [Fact]
    public async Task RetiringARuleWhoseReservationChangedElsewhereAfterItWasReadStillTakesItAway()
    {
        Rule going = WrittenForRetirement(0x76);
        await AddAsync(going);
        Reservation standing = await StandingAsync(9241, going.Id);

        await using (AsyncServiceScope scope = StandingInForRetirement())
        {
            await Reservations(scope).FindAsync(standing.Id, Cancel);
            await ChangedElsewhereAsync(standing.Id);

            Assert.NotNull(await Applying(scope).RetiredAsync(going.Id, Cancel));
        }

        Assert.Null(await FindAsyncForRetirement(standing.Id));
        Assert.Null(await ReadRuleAsync(going.Id));
    }

    [Fact]
    public async Task SwitchingARuleOffWhenItsReservationChangedElsewhereAfterItWasReadStillPullsItBack()
    {
        Rule going = WrittenForRetirement(0x77);
        await AddAsync(going);
        Reservation standing = await StandingAsync(9251, going.Id);
        await VisitedAsyncForRetirement();

        await using (AsyncServiceScope scope = StandingInForRetirement())
        {
            await Reservations(scope).FindAsync(standing.Id, Cancel);
            await ChangedElsewhereAsync(standing.Id);

            IReadOnlyList<Reservation> dropped = await Applying(scope).DroppedAsync(going.Id, Cancel);

            Assert.Equal([standing.Id], dropped.Select(reservation => reservation.Id));
        }

        Assert.Null(await FindAsyncForRetirement(standing.Id));
    }

    [Fact]
    public async Task ARuleThatIsNotThereLeavesTheLedgerAsItWas()
    {
        Rule staying = WrittenForRetirement(0x74);
        await AddAsync(staying);
        Reservation standing = await StandingAsync(9221, staying.Id);

        await using (AsyncServiceScope scope = StandingInForRetirement())
        {
            Assert.Null(await Applying(scope).RetiredAsync(
                new RuleId(new Guid("000000ff-0000-0000-0000-000000000000")),
                Cancel));
        }

        Assert.Equal(staying.Id, (await FindAsyncForRetirement(standing.Id))!.RuleId);
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    private AsyncServiceScope StandingIn()
    {
        TuningByService tuning = new();
        tuning.Answer(Listed, TuningParameters.Terrestrial(41));

        ServiceProvider standing = database.Infrastructure(replace =>
        {
            replace.AddSingleton<IBroadcastStreamDirectory>(new HeldStreams([Terrestrial()]));
            replace.AddSingleton<IBroadcastServiceRepository>(new HeldServices());
            replace.AddSingleton<IRecordingRepository>(new NoRecordings());
            replace.AddSingleton<ITunerCapacityDirectory>(new HeldSeating(Seats()));
            replace.AddSingleton<IServiceTuningDirectory>(tuning);
            replace.AddSingleton<IAppEventPublisher>(new SilentEvents());
            replace.AddSingleton<TimeProvider>(new FixedClock(Now));
        });
        standingIn.Add(standing);

        return standing.CreateAsyncScope();
    }

    private static RuleApplicationService Applying(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<RuleApplicationService>();

    private async Task<Programme> SownAsync(int carried, string name, DateTime? startsAt = null)
    {
        Programme programme = Sown(carried, name, revision: 0, startsAt: startsAt);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Programmes(scope).AddAsync(programme, Cancel);

        return programme;
    }

    private static Programme Sown(int carried, string name, long revision = 1, DateTime? startsAt = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
            new TransportStreamId(Carried),
            startsAt ?? Ahead,
            (startsAt ?? Ahead).AddHours(1),
            name,
            "what it is about",
            false,
            Now,
            revision: revision);

    private static Reservation Standing(Programme programme, RuleId ruleId)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

    private async Task AddAsync(Rule rule)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Rules(scope).AddAsync(rule, Cancel);
    }

    private async Task AddAsync(Reservation reservation)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Reservations(scope).AddAsync(reservation, Cancel);
    }

    private async Task VisitedAsync(VisitOutcome outcome)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Visits(scope).SaveAsync(
            StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)),
            Cancel);
    }

    private async Task<Reservation?> FindAsync(Programme programme)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Reservations(scope).FindByProgrammeAsync(
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            Cancel);
    }

    private static Rule Written(int identifier, string query)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            $"rule {identifier:x}",
            new RuleQuery(query),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static TunerCapacity Seats()
        => new(
            [
                .. Enumerable.Range(0, 8).Select(index =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
            ],
            []);

    private static BroadcastStream Terrestrial()
        => new(
            new NetworkId(Network),
            new TransportStreamId(Carried),
            TuningParameters.Terrestrial(41),
            [new ServiceId(Listed)]);

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IProgrammeRepository Programmes(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();

    private static IReservationRepository Reservations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRepository>();

    private static IRuleRepository Rules(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRuleRepository>();

    private static IStreamVisitRepository Visits(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IStreamVisitRepository>();

    private async Task<IReadOnlyList<Guid>> OrphanedAndStandingAmongAsync(params Reservation[] made)
    {
        ReservationId[] asked = [.. made.Select(reservation => reservation.Id)];

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Context(scope).Set<Reservation>()
            .Where(reservation => asked.Contains(reservation.Id))
            .Where(reservation => reservation.RuleId == null)
            .Where(reservation => reservation.StartedAt == null)
            .Where(reservation => reservation.State == ReservationState.Scheduled
                                  || reservation.State == ReservationState.Conflict)
            .Select(reservation => reservation.Id.Value)
            .ToListAsync(Cancel);
    }

    private AsyncServiceScope StandingInForRetirement()
    {
        TuningByService tuning = new() { Otherwise = Tunable() };

        ServiceProvider standing = database.Infrastructure(replace =>
        {
            replace.AddSingleton<IBroadcastStreamDirectory>(new HeldStreams([TerrestrialForRetirement()]));
            replace.AddSingleton<IBroadcastServiceRepository>(new HeldServices());
            replace.AddSingleton<IRecordingRepository>(new NoRecordings());
            replace.AddSingleton<ITunerCapacityDirectory>(new HeldSeating(Seats()));
            replace.AddSingleton<IServiceTuningDirectory>(tuning);
            replace.AddSingleton<IAppEventPublisher>(new SilentEvents());
            replace.AddSingleton<TimeProvider>(new FixedClock(Now));
        });
        standingIn.Add(standing);

        return standing.CreateAsyncScope();
    }

    private async Task<Reservation> ClaimedAsync(Reservation reservation)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.True(await Contract(scope).ClaimAsync(
            reservation.Id,
            Now.AddMinutes(-3),
            Cancel));

        return reservation;
    }

    private async Task<Reservation> StandingAsync(
        int carried,
        RuleId ruleId,
        ReservationState state = ReservationState.Scheduled)
    {
        DateTime opens = AheadForRetirement.AddHours(carried % 32);
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(
                new NetworkId(Network),
                new ServiceId(ListedForRetirement),
                new EventId(carried),
                opens),
            ruleId,
            Priority.Default,
            opens,
            opens.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                $"a broadcast {carried}",
                "what it is about",
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            state,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now,
            state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Reservations(scope).AddAsync(reservation, Cancel);

        return reservation;
    }

    private async Task ChangedElsewhereAsync(ReservationId id)
    {
        await using AsyncServiceScope elsewhere = container.CreateAsyncScope();
        IReservationRepository repository = Reservations(elsewhere);
        Reservation found = (await repository.FindAsync(id, Cancel))!;
        found.Reprioritise(new Priority(55));
        await repository.SaveAllAsync([found], Cancel);
    }

    private async Task VisitedAsyncForRetirement()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Visits(scope).SaveAsync(
            StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                VisitOutcome.Complete,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)),
            Cancel);
    }

    private async Task<Reservation?> FindAsyncForRetirement(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Reservations(scope).FindAsync(id, Cancel);
    }

    private async Task<Rule?> ReadRuleAsync(RuleId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Rules(scope).FindAsync(id, Cancel);
    }

    private static Rule WrittenForRetirement(int identifier)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            $"rule {identifier:x}",
            new RuleQuery("keyword=upland"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static TuningResolution Tunable()
        => TuningResolution.Tunable(
            new CandidateChannelId(Guid.NewGuid()),
            TuningParameters.Terrestrial(41),
            impaired: false);

    private static BroadcastStream TerrestrialForRetirement()
        => new(
            new NetworkId(Network),
            new TransportStreamId(Carried),
            TuningParameters.Terrestrial(41),
            [new ServiceId(ListedForRetirement)]);

    private static IReservationRecordingContract Contract(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRecordingContract>();
}
