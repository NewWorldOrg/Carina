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

    ProgrammeMoved = 5,

    ProgrammeGone = 6,

    ProgrammeReturned = 7,

    Retried = 8,

    GaveUpRetrying = 9,
}

/// <summary>
/// A line in the ledger either settles the reservation, saying what became of the recording, or
/// notes something that happened on the way to one. Only a settling line takes a reservation out of
/// the run that judges what became of it: a broadcast that slipped by a few minutes is most of
/// them, and a reservation held back by that line would never be written down as missed. Every
/// classification is named in one list or the other, and a test holds that.
/// </summary>
public static class ReservationOutcomeKinds
{
    public static readonly IReadOnlyList<ReservationOutcomeKind> Settling =
    [
        ReservationOutcomeKind.Competing,
        ReservationOutcomeKind.Missed,
        ReservationOutcomeKind.RecordingFailure,
    ];

    public static readonly IReadOnlyList<ReservationOutcomeKind> AlongTheWay =
    [
        ReservationOutcomeKind.TuneFailure,
        ReservationOutcomeKind.ProgrammeMoved,
        ReservationOutcomeKind.ProgrammeGone,
        ReservationOutcomeKind.ProgrammeReturned,
        ReservationOutcomeKind.Retried,
        ReservationOutcomeKind.GaveUpRetrying,
    ];
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

    public IReadOnlyList<RecordingFault> Faults { get; private set; } = [];

    public IReadOnlyList<Guid> RecordedInstead { get; private set; } = [];

    public DateTime OccurredAt { get; private set; }

    public RetryResult? RetryResult { get; private set; }

    public RetryGiveUp? GaveUpBecause { get; private set; }

    /// <summary>
    /// When the recording this line names as <see cref="RecordingFault.ScramblingUnresolved"/> was
    /// descrambled, or null while it has not been.
    /// </summary>
    public DateTime? DescrambledAt { get; private set; }

    /// <summary>
    /// Whether this line names <see cref="RecordingFault.ScramblingUnresolved"/> and has not been
    /// descrambled since.
    /// </summary>
    public bool LeftScrambled => NamesScrambling(Faults) && DescrambledAt is null;

    public static ReservationOutcome Record(
        ReservationOutcomeId id,
        Reservation reservation,
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<RecordingFault> faults,
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
            faults,
            recordedInstead,
            at);
    }

    public static ReservationOutcome RecordRetry(
        ReservationOutcomeId id,
        Reservation reservation,
        RetryAttempt attempt,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(attempt);

        return Rehydrate(
            id,
            reservation.Id,
            reservation.Programme,
            reservation.SnapshotName,
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            reservation.Priority,
            reservation.RuleId,
            ReservationOutcomeKind.Retried,
            attempt.TuneFailure,
            null,
            attempt.Faults,
            [],
            at,
            attempt.Result);
    }

    public static ReservationOutcome RecordGivingUp(
        ReservationOutcomeId id,
        Reservation reservation,
        RetryGiveUp because,
        TuneFailureKind? structural,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        IReadOnlyList<RecordingFault> faults = because switch
        {
            RetryGiveUp.NotTransient => [RecordingFault.TuneFailed],
            RetryGiveUp.PrecheckFailed => [RecordingFault.RefusedByDiskPrecheck],
            _ => [],
        };

        return Rehydrate(
            id,
            reservation.Id,
            reservation.Programme,
            reservation.SnapshotName,
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            reservation.Priority,
            reservation.RuleId,
            ReservationOutcomeKind.GaveUpRetrying,
            structural,
            null,
            faults,
            [],
            at,
            gaveUpBecause: because);
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
        IReadOnlyList<RecordingFault> faults,
        IReadOnlyList<Guid> recordedInstead,
        DateTime occurredAt,
        RetryResult? retryResult = null,
        RetryGiveUp? gaveUpBecause = null,
        DateTime? descrambledAt = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(snapshotName);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(faults);
        ArgumentNullException.ThrowIfNull(recordedInstead);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An outcome names a classification the ledger holds.");
        }

        if (tuneFailure is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(tuneFailure), tuneFailure, "A tune failure is one of the four kinds.");
        }

        if (retryResult is { } result && !Enum.IsDefined(result))
        {
            throw new ArgumentOutOfRangeException(nameof(retryResult), retryResult, "A retry came to one of the results the ledger holds.");
        }

        if (gaveUpBecause is { } reason && !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(gaveUpBecause), gaveUpBecause, "Giving up names one of the reasons the ledger holds.");
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

        foreach (RecordingFault fault in faults)
        {
            if (!Enum.IsDefined(fault))
            {
                throw new ArgumentOutOfRangeException(nameof(faults), fault, "A fault is one the ledger holds.");
            }
        }

        if (kind is ReservationOutcomeKind.TuneFailure && !faults.Contains(RecordingFault.TuneFailed))
        {
            throw new ArgumentException(
                "A tune failure names the class the recorder gave it, so the two cannot disagree.",
                nameof(faults));
        }

        if (kind is ReservationOutcomeKind.RecordingFailure
            && recordingOutcome is Recordings.RecordingOutcome.Complete
            && !NamesScrambling(faults))
        {
            throw new ArgumentException(
                "A recording that came out whole is a failure only when it was left scrambled, and says so.",
                nameof(faults));
        }

        if (descrambledAt is { } descrambled
            && (!NamesScrambling(faults) || descrambled < occurredAt))
        {
            throw new ArgumentException(
                "Only a line that named the scrambling says when it was descrambled, and that is after the line was written.",
                nameof(descrambledAt));
        }

        RefuseARetryThatDoesNotSayWhatCameOfIt(kind, tuneFailure, faults, retryResult, gaveUpBecause);

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
            Faults = [.. faults],
            RecordedInstead = recordedInstead,
            OccurredAt = UtcTimes.Required(occurredAt, nameof(occurredAt)),
            RetryResult = retryResult,
            GaveUpBecause = gaveUpBecause,
            DescrambledAt = UtcTimes.Optional(descrambledAt, nameof(descrambledAt)),
        };
    }

    /// <summary>
    /// Lifts <see cref="LeftScrambled"/> from a line that names
    /// <see cref="RecordingFault.ScramblingUnresolved"/>, keeping everything else the line says.
    /// </summary>
    /// <exception cref="InvalidOperationException">The line is not <see cref="LeftScrambled"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="at"/> is not UTC or is before the line was written.</exception>
    public void Descrambled(DateTime at)
    {
        if (!LeftScrambled)
        {
            throw new InvalidOperationException(
                "Only a line that names the scrambling, and has not been descrambled since, is descrambled.");
        }

        DateTime descrambled = UtcTimes.Required(at, nameof(at));

        if (descrambled < OccurredAt)
        {
            throw new ArgumentException(
                $"A line is descrambled after it was written at {OccurredAt:O}.",
                nameof(at));
        }

        DescrambledAt = descrambled;
    }

    private static bool NamesScrambling(IReadOnlyList<RecordingFault> faults)
        => faults.Contains(RecordingFault.ScramblingUnresolved);

    private static void RefuseARetryThatDoesNotSayWhatCameOfIt(
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        IReadOnlyList<RecordingFault> faults,
        RetryResult? retryResult,
        RetryGiveUp? gaveUpBecause)
    {
        if ((kind is ReservationOutcomeKind.Retried) != (retryResult is not null))
        {
            throw new ArgumentException(
                "A retry is written down with what came of it, and no other line carries a result.",
                nameof(retryResult));
        }

        if ((kind is ReservationOutcomeKind.GaveUpRetrying) != (gaveUpBecause is not null))
        {
            throw new ArgumentException(
                "Giving up on trying again says why, and no other line carries a reason.",
                nameof(gaveUpBecause));
        }

        if (retryResult is Recordings.RetryResult.Started && (tuneFailure is not null || faults.Count > 0))
        {
            throw new ArgumentException(
                "A retry that started a session was not refused, so it names no failure.",
                nameof(faults));
        }

        if (kind is ReservationOutcomeKind.Retried or ReservationOutcomeKind.GaveUpRetrying
            && tuneFailure is not null
            && !faults.Contains(RecordingFault.TuneFailed))
        {
            throw new ArgumentException(
                "A line that names a tune failure names the class the recorder gave it, so the two cannot disagree.",
                nameof(faults));
        }

        if (kind is ReservationOutcomeKind.GaveUpRetrying
            && (gaveUpBecause is RetryGiveUp.NotTransient) != (tuneFailure is not null))
        {
            throw new ArgumentException(
                "Giving up names a tune failure exactly when that failure is the reason.",
                nameof(tuneFailure));
        }
    }
}
