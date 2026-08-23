using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed record RecordingTick(
    ReservationId Id,
    NetworkId NetworkId,
    ServiceId ServiceId,
    EventId EventId,
    DateTime ProgrammeStartsAt,
    string Name,
    Priority Priority,
    BroadcastGroupKey? BroadcastGroupKey,
    BroadcastGroupRole BroadcastGroupRole,
    DateTime EffectiveStartAt,
    DateTime EffectiveEndAt,
    bool EndAtConfirmed,
    DateTime? StartedAt)
{
    public bool InFlight => StartedAt is not null;

    public ProgrammeRef Programme => new(NetworkId, ServiceId, EventId, ProgrammeStartsAt);
}

public interface IReservationRecordingContract
{
    Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken);

    Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken);
}
