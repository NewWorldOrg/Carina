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
        bool pinned)
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
    }

    public ReservationId Id { get; }

    public ProgrammeRef Programme { get; }

    public Priority Priority { get; }

    public TuningParameters? Tuning { get; }

    public DateTime EffectiveStartAt { get; }

    public DateTime EffectiveEndAt { get; }

    public bool EndAtConfirmed { get; }

    public bool Pinned { get; }

    public static AllocationCandidate Of(Reservation reservation, TuningParameters? tuning)
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
            reservation.IsPinned);
    }
}
