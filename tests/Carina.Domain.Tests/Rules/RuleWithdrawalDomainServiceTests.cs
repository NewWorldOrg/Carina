using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Domain.Tests.Reservations;
using Carina.TestSupport;

using static Carina.Domain.Tests.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Rules;

public sealed class RuleWithdrawalDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private static readonly NetworkId Network = new(32736);

    private static readonly TransportStreamId Stream = new(32736);

    private readonly HeldReservationLedger reservations = new();

    private readonly HeldStreams streams = new(
        [new BroadcastStream(Network, Stream, TuningParameters.Terrestrial(27), [new ServiceId(1024)])]);

    private readonly HeldStreamVisits visits = new();

    private readonly HeldEpochs epochs = new();

    private readonly RuleId rule = RuleId.New();

    private RuleWithdrawalDomainService Withdrawal => new(reservations, streams, visits, epochs, new FixedTimeProvider(new DateTimeOffset(Now)));

    [Theory(DisplayName = "BR-RS-003: a rule takes back a reservation only once the guide of its stream has been read whole")]
    [InlineData(VisitOutcome.Complete, true)]
    [InlineData(VisitOutcome.BasicOnly, true)]
    [InlineData(VisitOutcome.Incomplete, false)]
    public async Task ARuleTakesBackOnlyOnceTheGuideWasReadWhole(VisitOutcome read, bool withdrawn)
    {
        Reservation made = Held(4001);
        visits.Visits.Add(StreamVisit.Record(Network, Stream, read, Now.AddHours(-1), TimeSpan.Zero));

        RuleWithdrawalGuard guard = await Withdrawal.GuardAsync(Grace, Cancel);

        Assert.Equal(withdrawn, guard.Lets(made, RuleWithdrawal.WhileTheRuleStands, Now));
        Assert.Equal(withdrawn, guard.Lets(made, RuleWithdrawal.WhenTheRuleIsSwitchedOff, Now));
        Assert.True(guard.Lets(made, RuleWithdrawal.WhenTheRuleIsDeleted, Now));
    }

    [Fact(DisplayName = "BR-RS-003: a reading finished no later than the guide was last thrown away does not count as read")]
    public async Task AReadingBeforeTheGuideWasThrownAwayDoesNotCount()
    {
        Reservation made = Held(4001);
        visits.Visits.Add(StreamVisit.Record(Network, Stream, VisitOutcome.Complete, Now.AddHours(-1), TimeSpan.Zero));
        await epochs.SaveAsync(CollectionEpoch.Rehydrate(CollectionEpoch.TheOnlyRow, 2, Now.AddHours(-1)), Cancel);

        RuleWithdrawalGuard guard = await Withdrawal.GuardAsync(Grace, Cancel);

        Assert.False(guard.Lets(made, RuleWithdrawal.WhenTheRuleIsSwitchedOff, Now));
    }

    [Fact(DisplayName = "BR-RS-003: a pass of the rules takes back what a standing rule no longer takes, and leaves what it still takes")]
    public async Task APassTakesBackWhatAStandingRuleNoLongerTakes()
    {
        Read();
        Reservation dropped = Held(4001);
        Reservation taken = Held(4002);

        IReadOnlyList<Reservation> leaving = await LeavingAsync(Pass(taken: [Key(taken)], seen: [Key(dropped), Key(taken)]));

        Assert.Equal([dropped], leaving);
    }

    [Fact(DisplayName = "BR-RS-003: a pass leaves the reservations of a rule that failed to match, and those whose listing it did not read")]
    public async Task APassLeavesWhatItCannotJudge()
    {
        Read();
        Reservation unread = Held(4001);
        RuleId faltering = RuleId.New();
        Reservation ofTheFaltering = Held(4002, faltering);

        RulePassReading pass = Pass(taken: [], seen: [Key(ofTheFaltering)]) with
        {
            Faulted = new HashSet<RuleId> { faltering },
            Standing = new HashSet<RuleId> { rule, faltering },
        };

        Assert.Empty(await LeavingAsync(pass));
        Assert.Equal([unread], await LeavingAsync(pass with { Sweeping = true }));
    }

    [Fact(DisplayName = "BR-RS-003: a pass takes back what a rule switched off made, ignoring the grace")]
    public async Task APassTakesBackWhatARuleSwitchedOffMadeIgnoringTheGrace()
    {
        Read();
        Reservation soon = Held(4001, startsAt: Now.AddMinutes(5));

        RulePassReading pass = Pass(taken: [], seen: [Key(soon)]) with { Standing = new HashSet<RuleId>() };

        Assert.Equal([soon], await LeavingAsync(pass));
        Assert.Empty(await LeavingAsync(Pass(taken: [], seen: [Key(soon)])));
    }

    [Fact(DisplayName = "BR-RD-010: a reservation whose start moved still stands for the broadcast the rule takes")]
    public async Task AReservationWhoseStartMovedStillStandsForTheBroadcast()
    {
        Read();
        Reservation moved = Held(4001);
        var announced = new ListingKey(32736, 1024, 4001, moved.ProgrammeStartsAt.AddMinutes(15));

        Assert.Empty(await LeavingAsync(Pass(taken: [announced], seen: [Key(moved)])));
    }

    [Fact(DisplayName = "BR-RD-010: a segment of a relayed broadcast stays while another segment is being recorded")]
    public async Task ASegmentStaysWhileAnotherIsBeingRecorded()
    {
        Read();
        var key = new BroadcastGroupKey("relay:32736-1024-4001");
        Reservation recording = Reservation.Rehydrate(
            ReservationId.New(),
            Programme(4001, Now.AddMinutes(-30)),
            rule,
            Priority.Default,
            Now.AddMinutes(-30),
            Now.AddMinutes(30),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            key,
            BroadcastGroupRole.RelaySegment,
            ReservationState.Scheduled,
            Now.AddMinutes(-30),
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);
        Reservation after = Reservation.Plan(
            ReservationId.New(),
            Programme(4002, Now.AddHours(1)),
            rule,
            Priority.Default,
            Now.AddHours(1),
            Now.AddHours(2),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            key,
            BroadcastGroupRole.RelaySegment,
            Now);
        reservations.Reservations.AddRange([recording, after]);

        Assert.Empty(await LeavingAsync(Pass(taken: [], seen: [Key(after)])));
    }

    [Fact(DisplayName = "BR-RS-003: a rule switched off takes back what it made, and one deleted keeps what is being recorded without it")]
    public async Task ARuleSwitchedOffOrDeletedTakesBackWhatItMay()
    {
        Read();
        Reservation waiting = Held(4001);
        Reservation recording = Rehydrated(ReservationState.Scheduled, Now, null, ruleId: rule);
        Reservation cancelled = Rehydrated(ReservationState.Cancelled, null, null, ruleId: rule);
        reservations.Reservations.AddRange([recording, cancelled]);
        RuleWithdrawalGuard guard = await Withdrawal.GuardAsync(Grace, Cancel);

        Assert.Equal([waiting], await Withdrawal.SwitchedOffAsync(rule, guard, Now, Cancel));

        RuleRetiring retiring = await Withdrawal.RetiringAsync(rule, guard, Now, Cancel);

        Assert.Equal([waiting], retiring.Withdrawn);
        Assert.Equal([recording, cancelled], retiring.Kept);
    }

    [Fact(DisplayName = "BR-RD-011: the rehearsal of a rule names what it would take back while standing and what it would take back if deleted")]
    public async Task TheRehearsalNamesWhatARuleWouldTakeBack()
    {
        Read();
        Reservation dropped = Held(4001);
        Reservation taken = Held(4002);
        Reservation soon = Held(4003, startsAt: Now.AddMinutes(5));
        RuleWithdrawalGuard guard = await Withdrawal.GuardAsync(Grace, Cancel);

        RuleWithdrawalRehearsal rehearsed = await Withdrawal.RehearseAsync(rule, [Key(taken)], guard, Now, Cancel);

        Assert.Equal([dropped], rehearsed.Withdrawing);
        Assert.Equal([dropped, taken, soon], rehearsed.Sweeping);
    }

    private static ListingKey Key(Reservation reservation) => ListingKey.Of(reservation);

    private void Read()
        => visits.Visits.Add(StreamVisit.Record(Network, Stream, VisitOutcome.Complete, Now.AddHours(-1), TimeSpan.Zero));

    private Reservation Held(int eventId, RuleId? by = null, DateTime? startsAt = null)
    {
        Reservation reservation = Planned(ruleId: by ?? rule, programme: Programme(eventId, startsAt ?? Now.AddHours(2).AddMinutes(eventId - 4001)));
        reservations.Reservations.Add(reservation);

        return reservation;
    }

    private RulePassReading Pass(IReadOnlyList<ListingKey> taken, IReadOnlyList<ListingKey> seen)
        => new(new HashSet<RuleId>(), new HashSet<RuleId> { rule }, taken, seen.ToHashSet(), false);

    private async Task<IReadOnlyList<Reservation>> LeavingAsync(RulePassReading pass)
        => await Withdrawal.LeavingAsync(pass, await Withdrawal.GuardAsync(Grace, Cancel), Now, Cancel);
}
