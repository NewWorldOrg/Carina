using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public enum RetryMove
{
    Retry = 1,

    Wait = 2,

    GiveUp = 3,
}

/// <summary>
/// What is known about one reservation whose start failed, at the moment the recorder comes back to
/// it: every class its starts have failed in so far, and the attempts made after the first failure,
/// not counting it.
/// </summary>
public readonly record struct RetrySighting(
    IReadOnlyList<TuneFailureKind> FailureClasses,
    bool StillOnAir,
    int AttemptsSoFar,
    DateTime LastAttemptAt,
    bool CandidateNeedsAttention,
    DateTime? CandidateRestsUntil,
    bool PrecheckFailed);

public sealed record RetryVerdict
{
    private RetryVerdict(RetryMove move, RetryGiveUp? reason, DateTime? notBefore)
    {
        Move = move;
        Reason = reason;
        NotBefore = notBefore;
    }

    public static RetryVerdict Retry { get; } = new(RetryMove.Retry, null, null);

    public RetryMove Move { get; }

    public RetryGiveUp? Reason { get; }

    public DateTime? NotBefore { get; }

    public static RetryVerdict WaitUntil(DateTime notBefore)
        => new(RetryMove.Wait, null, UtcTimes.Required(notBefore, nameof(notBefore)));

    public static RetryVerdict GiveUp(RetryGiveUp reason)
        => Enum.IsDefined(reason)
            ? new RetryVerdict(RetryMove.GiveUp, reason, null)
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "Giving up names one of the reasons there are.");
}

/// <summary>
/// What the ledger says about the starts of one reservation: the classes they failed in, how many
/// attempts followed the first failure, when the latest of them was made, and whether trying again
/// was already given up.
/// </summary>
public sealed record RetryHistory(
    IReadOnlyList<TuneFailureKind> FailureClasses,
    int AttemptsSoFar,
    DateTime LastAttemptAt,
    bool GivenUp);

/// <summary>
/// Whether a recording whose start failed is started again. Only the start is ever tried again.
/// </summary>
/// <remarks>
/// Only the two passing failures, a tuner that did not lock and a lock that brought no data, are
/// tried again, and only while the programme is still on the air. A stream that is not the one
/// expected, a disk the precheck found no room on, and a channel already set aside as needing
/// attention are given up on at once; the reasons are asked in that order. After that come the count
/// and the pause since the last attempt, and a channel that is backing off is waited for however
/// short the pause is.
/// </remarks>
public static class StartRetry
{
    public static readonly IReadOnlyList<TuneFailureKind> Transient =
    [
        TuneFailureKind.NoLock,
        TuneFailureKind.NoData,
    ];

    public static RetryVerdict For(RetrySighting sighting, RetryPolicy policy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        IReadOnlyList<TuneFailureKind> classes = sighting.FailureClasses
            ?? throw new ArgumentException("A sighting names the classes its starts failed in.", nameof(sighting));

        if (classes.Count is 0)
        {
            throw new ArgumentException(
                "A start is tried again only after it failed in one of the four classes, so there is one to weigh.",
                nameof(sighting));
        }

        foreach (TuneFailureKind kind in classes)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(sighting), kind, "A tune failure is one of the four kinds.");
            }
        }

        if (sighting.AttemptsSoFar < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sighting),
                sighting.AttemptsSoFar,
                "A count of attempts already made is not negative.");
        }

        DateTime at = UtcTimes.Required(now, nameof(now));
        DateTime last = UtcTimes.Required(sighting.LastAttemptAt, nameof(sighting));
        DateTime? rests = UtcTimes.Optional(sighting.CandidateRestsUntil, nameof(sighting));

        if (StructuralAmong(classes) is not null)
        {
            return RetryVerdict.GiveUp(RetryGiveUp.NotTransient);
        }

        if (sighting.PrecheckFailed)
        {
            return RetryVerdict.GiveUp(RetryGiveUp.PrecheckFailed);
        }

        if (sighting.CandidateNeedsAttention)
        {
            return RetryVerdict.GiveUp(RetryGiveUp.CandidateNeedsAttention);
        }

        if (!sighting.StillOnAir)
        {
            return RetryVerdict.GiveUp(RetryGiveUp.BroadcastOver);
        }

        if (sighting.AttemptsSoFar >= policy.MostAttempts)
        {
            return RetryVerdict.GiveUp(RetryGiveUp.AttemptsSpent);
        }

        DateTime notBefore = last + policy.BetweenAttempts;

        if (rests is { } resting && resting > notBefore)
        {
            notBefore = resting;
        }

        return at < notBefore ? RetryVerdict.WaitUntil(notBefore) : RetryVerdict.Retry;
    }

    public static TuneFailureKind? StructuralAmong(IReadOnlyList<TuneFailureKind> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        return classes
            .Where(kind => !Transient.Contains(kind))
            .Select(kind => (TuneFailureKind?)kind)
            .FirstOrDefault();
    }

    /// <summary>
    /// The retry history of a reservation, or null when none of its starts has failed in a class yet.
    /// </summary>
    public static RetryHistory? HistoryOf(IReadOnlyList<ReservationOutcome> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        ReservationOutcome[] tried =
        [
            .. lines.Where(line => line.Kind is ReservationOutcomeKind.TuneFailure or ReservationOutcomeKind.Retried),
        ];

        if (!tried.Any(line => line.Kind is ReservationOutcomeKind.TuneFailure))
        {
            return null;
        }

        return new RetryHistory(
            [
                .. tried
                    .OrderBy(line => line.OccurredAt)
                    .Where(line => line.TuneFailure is not null)
                    .Select(line => line.TuneFailure!.Value)
                    .Distinct(),
            ],
            tried.Count(line => line.Kind is ReservationOutcomeKind.Retried),
            tried.Max(line => line.OccurredAt),
            lines.Any(line => line.Kind is ReservationOutcomeKind.GaveUpRetrying));
    }
}
