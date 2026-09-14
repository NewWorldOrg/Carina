using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Reservations;

public sealed record AllocationCandidate
{
    public AllocationCandidate(
        ReservationId id,
        ProgrammeRef programme,
        Priority priority,
        TuningParameters? tuning,
        DateTime effectiveStartAt,
        DateTime effectiveEndAt,
        bool endAtConfirmed,
        bool pinned,
        DateTime? heldUntil = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(priority);

        DateTime opens = UtcTimes.Required(effectiveStartAt, nameof(effectiveStartAt));
        DateTime closes = UtcTimes.Required(effectiveEndAt, nameof(effectiveEndAt));

        if (closes <= opens)
        {
            throw new ArgumentException(
                "A candidate holds a tuner over a window that ends after it opens.",
                nameof(effectiveEndAt));
        }

        Id = id;
        Programme = programme;
        Priority = priority;
        Tuning = tuning;
        EffectiveStartAt = opens;
        EffectiveEndAt = closes;
        EndAtConfirmed = endAtConfirmed;
        Pinned = pinned;
        HeldUntil = heldUntil is { } held ? UtcTimes.Required(held, nameof(heldUntil)) : null;
    }

    public ReservationId Id { get; }

    public ProgrammeRef Programme { get; }

    public Priority Priority { get; }

    public TuningParameters? Tuning { get; }

    public DateTime EffectiveStartAt { get; }

    public DateTime EffectiveEndAt { get; }

    public bool EndAtConfirmed { get; }

    public bool Pinned { get; }

    /// <summary>
    /// How far the recording this reservation has already started is actually promised, when that
    /// is further than the reservation's own end. A recording that followed its programme past the
    /// end the reservation was planned for holds its tuner for the window it was granted, not the
    /// one the reservation still says: without this the planner would seat the next reservation on
    /// a tuner that is not free yet and call it secured until the moment it is refused.
    /// </summary>
    public DateTime? HeldUntil { get; }

    public static AllocationCandidate Of(
        Reservation reservation,
        TuningParameters? tuning,
        DateTime? heldUntil = null)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return new AllocationCandidate(
            reservation.Id,
            reservation.Programme,
            reservation.Priority,
            tuning,
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            reservation.EndAtConfirmed,
            reservation.IsPinned,
            heldUntil);
    }
}
