using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Rules;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RuleApplicationLandsInTheLedgerTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ahead = Now.AddDays(20);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 3049;

    [Fact]
    public async Task AReservationARuleMadeIsInTheLedgerUnderTheRuleThatMadeIt()
    {
        Rule rule = Written(0x51, "keyword=upland");
        Programme taken = await SownAsync(9101, "an upland walk");
        Programme left = await SownAsync(9102, "a lowland drive");
        await VisitedAsync(VisitOutcome.Complete);
        await AddAsync(rule);

        await using (CarinaDbContext context = database.Open())
        {
            RuleApplicationRun run = await ApplyingOver(context).EverythingAsync(Cancel);

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

        await using (CarinaDbContext context = database.Open())
        {
            RuleApplicationRun run = await ApplyingOver(context).EverythingAsync(Cancel);

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

        await using (CarinaDbContext context = database.Open())
        {
            RuleApplicationRun run = await ApplyingOver(context).EverythingAsync(Cancel);

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

        await using (CarinaDbContext context = database.Open())
        {
            IReadOnlyList<Reservation> dropped = await ApplyingOver(context).DroppedAsync(rule.Id, Cancel);

            Assert.Contains(dropped, gone => gone.Id.Equals(standing.Id));
        }

        Assert.Null(await FindAsync(going));

        await using CarinaDbContext removing = database.Open();
        var repository = new RuleRepository(removing);
        await repository.RemoveAsync((await repository.FindAsync(rule.Id, Cancel))!, Cancel);

        Assert.Null(await repository.FindAsync(rule.Id, Cancel));
    }

    private RuleApplicationService ApplyingOver(CarinaDbContext context)
    {
        var streams = new HeldStreams([Terrestrial()]);
        var reservations = new ReservationRepository(context);
        var tuning = new TuningByService();
        tuning.Answer(Listed, TuningParameters.Terrestrial(41));

        return new RuleApplicationService(
            new RuleRepository(context),
            new ProgrammeRepository(context),
            reservations,
            new StreamVisitRepository(context),
            streams,
            new ReservationSchedulingService(
                reservations,
                new HeldSeating(Seats()),
                tuning,
                new DatabaseAtomicWrite(context),
                RollingHorizon.Default,
                new FixedClock(Now)),
            new RuleMatcher(new ProgrammeSearchScope(streams, new HeldServices()), new FixedClock(Now)),
            new RuleApplicationSettings(),
            new FixedClock(Now));
    }

    private async Task<Programme> SownAsync(int carried, string name)
    {
        Programme programme = Sown(carried, name);

        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        programme.MarkRevision(await repository.NextRevisionAsync(Cancel));
        await repository.AddAsync(programme, Cancel);

        return programme;
    }

    private static Programme Sown(int carried, string name)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
            new TransportStreamId(Carried),
            Ahead,
            Ahead.AddHours(1),
            name,
            "what it is about",
            false,
            Now,
            revision: 1);

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
            new ProgrammeSnapshot(programme.Name, programme.Summary, string.Empty, [], Now),
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
        await using CarinaDbContext context = database.Open();
        await new RuleRepository(context).AddAsync(rule, Cancel);
    }

    private async Task AddAsync(Reservation reservation)
    {
        await using CarinaDbContext context = database.Open();
        await new ReservationRepository(context).AddAsync(reservation, Cancel);
    }

    private async Task VisitedAsync(VisitOutcome outcome)
    {
        await using CarinaDbContext context = database.Open();
        await new StreamVisitRepository(context).SaveAsync(
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
        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).FindByProgrammeAsync(
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
}
