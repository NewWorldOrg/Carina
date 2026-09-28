using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed record RecordingTick(
    ReservationId Id,
    NetworkId NetworkId,
    ServiceId ServiceId,
    EventId EventId,
    DateTime ProgrammeStartsAt,
    ProgrammeSnapshot Snapshot,
    Priority Priority,
    BroadcastGroupKey? BroadcastGroupKey,
    BroadcastGroupRole BroadcastGroupRole,
    DateTime EffectiveStartAt,
    DateTime EffectiveEndAt,
    bool EndAtConfirmed,
    TimeSpan MarginAfter,
    DateTime? StartedAt,
    bool EncodeWhenRecorded = true)
{
    public bool InFlight => StartedAt is not null;

    public ProgrammeRef Programme => new(NetworkId, ServiceId, EventId, ProgrammeStartsAt);
}

public interface IReservationRecordingContract
{
    /// <summary>
    /// The reservations in flight, and those whose effective window has not closed at <paramref name="at"/>
    /// and opens no later than <paramref name="ahead"/> after it.
    /// </summary>
    Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, TimeSpan ahead, CancellationToken cancellationToken);

    Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt, CancellationToken cancellationToken);
}
