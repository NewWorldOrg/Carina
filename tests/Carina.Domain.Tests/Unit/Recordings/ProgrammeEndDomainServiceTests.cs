using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class ProgrammeEndDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = RecordingFactory.Now;

    private readonly HeldProgrammes programmes = new();

    private readonly ReservationsById reservations = new();

    private ProgrammeEndDomainService Service => new(programmes, reservations);

    [Fact(DisplayName = "BR-CD-005: a recorded programme ends when the guide says it does, whatever its reservation says")]
    public async Task ARecordedProgrammeEndsWhenTheGuideSays()
    {
        Reservation confirmed = Reserved(Now.AddMinutes(40), confirmed: true);
        Recording recording = RecordingFactory.Started(reservationId: confirmed.Id);
        programmes.Programmes.Add(Announced(recording, Now.AddMinutes(25)));

        Assert.Equal(Now.AddMinutes(25), await Service.EndsAtAsync(recording, Cancel));
    }

    [Fact(DisplayName = "BR-CD-005: without the guide a recorded programme ends when its reservation says, once that end was confirmed")]
    public async Task WithoutTheGuideItEndsWhenItsConfirmedReservationSays()
    {
        Reservation confirmed = Reserved(Now.AddMinutes(40), confirmed: true);

        Assert.Equal(Now.AddMinutes(40), await Service.EndsAtAsync(RecordingFactory.Started(reservationId: confirmed.Id), Cancel));
    }

    [Fact(DisplayName = "BR-CD-005: without the guide or a confirmed end a recorded programme ends where its recording was promised to")]
    public async Task OtherwiseItEndsWhereTheRecordingWasPromised()
    {
        Reservation provisional = Reserved(Now.AddMinutes(40), confirmed: false);
        Recording unconfirmed = RecordingFactory.Started(reservationId: provisional.Id);
        Recording unreserved = RecordingFactory.Started();
        Recording reservationGone = RecordingFactory.Started(reservationId: ReservationId.New());

        Assert.Equal(unconfirmed.PromisedWindowEnd, await Service.EndsAtAsync(unconfirmed, Cancel));
        Assert.Equal(unreserved.PromisedWindowEnd, await Service.EndsAtAsync(unreserved, Cancel));
        Assert.Equal(reservationGone.PromisedWindowEnd, await Service.EndsAtAsync(reservationGone, Cancel));
        Assert.Equal(Now.AddMinutes(55), unreserved.PromisedWindowEnd);
    }

    [Fact(DisplayName = "BR-CD-005: an end from the guide no later than the programme starts is passed over")]
    public async Task AnEndFromTheGuideNoLaterThanTheStartIsPassedOver()
    {
        Recording recording = RecordingFactory.Started();
        programmes.Programmes.Add(Announced(recording, recording.ProgrammeStartsAt));

        Assert.Equal(recording.PromisedWindowEnd, await Service.EndsAtAsync(recording, Cancel));
    }

    private Reservation Reserved(DateTime endAt, bool confirmed)
    {
        ProgrammeRef programme = RecordingFactory.Programme();
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            programme.StartsAt,
            endAt,
            confirmed,
            Margin.None,
            Margin.None,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);
        reservations.Held.Add(reservation);

        return reservation;
    }

    private static Programme Announced(Recording recording, DateTime endsAt)
        => Programme.Rehydrate(
            new ProgrammeId(recording.NetworkId, recording.ServiceId, recording.EventId),
            new TransportStreamId(32736),
            recording.ProgrammeStartsAt,
            endsAt,
            "A programme",
            string.Empty,
            false,
            Now);

    private sealed class ReservationsById : IReservationRepository
    {
        public List<Reservation> Held { get; } = [];

        public Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken)
            => Task.FromResult(Held.FirstOrDefault(reservation => reservation.Id.Equals(id)));

        public Task<PaginatedList<Reservation>> ListAsync(ReservationQuery query, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ReservationHealth> HealthAsync(DateTime at, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Reservation>> ListPendingAsync(ReservationWindow window, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReservationAwaitingOutcome>> ListAwaitingOutcomeAsync(DateTime through, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Reservation>> ListClaimedOverAsync(ReservationWindow window, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(BroadcastGroupKey key, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Reservation reservation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(Reservation reservation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ReservationDiscard> DiscardAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
