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

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Rules;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RuleRetirementLandsInTheLedgerTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ahead = Now.AddDays(24);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 3149;

    [Fact]
    public async Task RetiringARuleLeavesNoStandingReservationBehindThatNothingOwnsAnyMore()
    {
        Rule going = Written(0x71);
        Rule staying = Written(0x72);
        await AddAsync(going);
        await AddAsync(staying);

        Reservation standing = await StandingAsync(9201, going.Id);
        Reservation recording = await ClaimedAsync(await StandingAsync(9202, going.Id));
        Reservation cancelled = await StandingAsync(9203, going.Id, ReservationState.Cancelled);
        Reservation another = await StandingAsync(9204, staying.Id);

        await using (CarinaDbContext context = database.Open())
        {
            Assert.NotNull(await RetiringOver(context).RetiredAsync(going.Id, Cancel));
        }

        Assert.Null(await FindAsync(standing.Id));
        Assert.NotNull(await FindAsync(recording.Id));
        Assert.NotNull(await FindAsync(cancelled.Id));
        Assert.Equal(staying.Id, (await FindAsync(another.Id))!.RuleId);
        Assert.Empty(await OrphanedAndStandingAmongAsync(standing, recording, cancelled, another));
        Assert.Null(await ReadRuleAsync(going.Id));
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    [Fact]
    public async Task WhatIsKeptWhenTheRuleGoesIsLetGoOfItBeforeTheRuleIsTakenAway()
    {
        Rule going = Written(0x73);
        await AddAsync(going);
        Reservation recording = await ClaimedAsync(await StandingAsync(9211, going.Id));
        Reservation cancelled = await StandingAsync(9212, going.Id, ReservationState.Cancelled);

        await using (CarinaDbContext context = database.Open())
        {
            Assert.NotNull(await RetiringOver(context).RetiredAsync(going.Id, Cancel));
        }

        Reservation? kept = await FindAsync(recording.Id);
        Reservation? held = await FindAsync(cancelled.Id);

        Assert.NotNull(kept);
        Assert.Null(kept.RuleId);
        Assert.True(kept.IsPinned);
        Assert.NotNull(held);
        Assert.Null(held.RuleId);
        Assert.Equal(ReservationState.Cancelled, held.State);
        Assert.Null(await ReadRuleAsync(going.Id));
    }

    [Fact]
    public async Task ARuleTakenOutFromUnderAReservationIsRefusedByTheDatabaseItself()
    {
        Rule staying = Written(0x75);
        await AddAsync(staying);
        await ClaimedAsync(await StandingAsync(9231, staying.Id));

        await using CarinaDbContext context = database.Open();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlRawAsync(
                "DELETE FROM rule WHERE id = {0}",
                [staying.Id.Value],
                Cancel));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refusal.SqlState);
        Assert.Equal("fk_reservation_rule_rule_id", refusal.ConstraintName);
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    [Fact]
    public async Task ARuleThatIsNotThereLeavesTheLedgerAsItWas()
    {
        Rule staying = Written(0x74);
        await AddAsync(staying);
        Reservation standing = await StandingAsync(9221, staying.Id);

        await using (CarinaDbContext context = database.Open())
        {
            Assert.Null(await RetiringOver(context).RetiredAsync(
                new RuleId(new Guid("000000ff-0000-0000-0000-000000000000")),
                Cancel));
        }

        Assert.Equal(staying.Id, (await FindAsync(standing.Id))!.RuleId);
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    private async Task<IReadOnlyList<Guid>> OrphanedAndStandingAmongAsync(params Reservation[] made)
    {
        ReservationId[] asked = [.. made.Select(reservation => reservation.Id)];

        await using CarinaDbContext context = database.Open();

        return await context.Set<Reservation>()
            .Where(reservation => asked.Contains(reservation.Id))
            .Where(reservation => reservation.RuleId == null)
            .Where(reservation => reservation.StartedAt == null)
            .Where(reservation => reservation.State == ReservationState.Scheduled
                                  || reservation.State == ReservationState.Conflict)
            .Select(reservation => reservation.Id.Value)
            .ToListAsync(Cancel);
    }

    private RuleApplicationService RetiringOver(CarinaDbContext context)
    {
        var reservations = new ReservationRepository(context);
        var streams = new HeldStreams([Terrestrial()]);

        return new RuleApplicationService(
            new RuleRepository(context),
            new ProgrammeRepository(context),
            reservations,
            new StreamVisitRepository(context),
            streams,
            new ReservationSchedulingService(
                reservations,
                new HeldSeating(Seats()),
                new TuningByService { Otherwise = Tunable() },
                new DatabaseAtomicWrite(context),
                RollingHorizon.Default,
                new FixedClock(Now)),
            new RuleMatcher(new ProgrammeSearchScope(streams, new HeldServices()), new FixedClock(Now)),
            new RuleApplicationSettings(),
            new DatabaseAtomicWrite(context),
            new FixedClock(Now));
    }

    private async Task<Reservation> ClaimedAsync(Reservation reservation)
    {
        await using CarinaDbContext context = database.Open();

        Assert.True(await new ReservationRecordingContract(context).ClaimAsync(
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
        DateTime opens = Ahead.AddHours(carried % 32);
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(
                new NetworkId(Network),
                new ServiceId(Listed),
                new EventId(carried),
                opens),
            ruleId,
            Priority.Default,
            opens,
            opens.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot($"a broadcast {carried}", "what it is about", string.Empty, [], Now),
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
            Now);

        await using CarinaDbContext context = database.Open();
        await new ReservationRepository(context).AddAsync(reservation, Cancel);

        return reservation;
    }

    private async Task AddAsync(Rule rule)
    {
        await using CarinaDbContext context = database.Open();
        await new RuleRepository(context).AddAsync(rule, Cancel);
    }

    private async Task<Reservation?> FindAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).FindAsync(id, Cancel);
    }

    private async Task<Rule?> ReadRuleAsync(RuleId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new RuleRepository(context).FindAsync(id, Cancel);
    }

    private static Rule Written(int identifier)
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
