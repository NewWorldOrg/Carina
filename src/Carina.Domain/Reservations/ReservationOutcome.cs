using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Rules;

namespace Carina.Domain.Reservations;

public enum ReservationOutcomeKind
{
    Competing = 1,

    Missed = 2,

    TuneFailure = 3,

    RecordingFailure = 4,
}

public sealed class ReservationOutcome
{
    private ReservationOutcome()
    {
    }

    public ReservationOutcomeId Id { get; private set; } = null!;

    public ReservationId ReservationId { get; private set; } = null!;

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public DateTime ProgrammeStartsAt { get; private set; }

    public string SnapshotName { get; private set; } = string.Empty;

    public DateTime EffectiveStartAt { get; private set; }

    public DateTime EffectiveEndAt { get; private set; }

    public Priority Priority { get; private set; } = null!;

    public RuleId? RuleId { get; private set; }

    public ReservationOutcomeKind Kind { get; private set; }

    public TuneFailureKind? TuneFailure { get; private set; }

    public RecordingOutcome? RecordingOutcome { get; private set; }

    public IReadOnlyList<Guid> RecordedInstead { get; private set; } = [];

    public DateTime OccurredAt { get; private set; }

    public static ReservationOutcome Record(
        ReservationOutcomeId id,
        Reservation reservation,
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<Guid> recordedInstead,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return Rehydrate(
            id,
            reservation.Id,
            reservation.Programme,
            reservation.SnapshotName,
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            reservation.Priority,
            reservation.RuleId,
            kind,
            tuneFailure,
            recordingOutcome,
            recordedInstead,
            at);
    }

    public static ReservationOutcome Rehydrate(
        ReservationOutcomeId id,
        ReservationId reservationId,
        ProgrammeRef programme,
        string snapshotName,
        DateTime effectiveStartAt,
        DateTime effectiveEndAt,
        Priority priority,
        RuleId? ruleId,
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<Guid> recordedInstead,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(snapshotName);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(recordedInstead);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An outcome names a classification the ledger holds.");
        }

        if (tuneFailure is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(tuneFailure), tuneFailure, "A tune failure is one of the four kinds.");
        }

        if (kind is ReservationOutcomeKind.TuneFailure && tuneFailure is null)
        {
            throw new ArgumentException(
                "A tune failure is recorded with the kind of failure it was.",
                nameof(tuneFailure));
        }

        if (kind is not ReservationOutcomeKind.Competing && recordedInstead.Count > 0)
        {
            throw new ArgumentException(
                "Only a reservation that lost a contest names what was recorded instead.",
                nameof(recordedInstead));
        }

        if (kind is ReservationOutcomeKind.RecordingFailure && recordingOutcome is null)
        {
            throw new ArgumentException(
                "A failure reported by recording carries the outcome recording wrote.",
                nameof(recordingOutcome));
        }

        return new ReservationOutcome
        {
            Id = id,
            ReservationId = reservationId,
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            ProgrammeStartsAt = programme.StartsAt,
            SnapshotName = snapshotName,
            EffectiveStartAt = UtcTimes.Required(effectiveStartAt, nameof(effectiveStartAt)),
            EffectiveEndAt = UtcTimes.Required(effectiveEndAt, nameof(effectiveEndAt)),
            Priority = priority,
            RuleId = ruleId,
            Kind = kind,
            TuneFailure = tuneFailure,
            RecordingOutcome = recordingOutcome,
            RecordedInstead = recordedInstead,
            OccurredAt = UtcTimes.Required(occurredAt, nameof(occurredAt)),
        };
    }
}
