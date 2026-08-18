using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public enum VisitOutcome
{
    Complete = 1,

    BasicOnly = 2,

    Incomplete = 3,

    Interrupted = 4,

    NoLock = 5,

    NoBytes = 6,
}

public sealed class StreamVisit
{
    private StreamVisit()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public DateTime LastAttemptedAt { get; private set; }

    public DateTime? LastCompletedAt { get; private set; }

    public VisitOutcome Outcome { get; private set; }

    public int ConsecutiveIncomplete { get; private set; }

    public int LastDurationMilliseconds { get; private set; }

    public static StreamVisit Rehydrate(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        DateTime lastAttemptedAt,
        DateTime? lastCompletedAt,
        VisitOutcome outcome,
        int consecutiveIncomplete,
        int lastDurationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveIncomplete);
        ArgumentOutOfRangeException.ThrowIfNegative(lastDurationMilliseconds);

        return new StreamVisit
        {
            NetworkId = networkId,
            TransportStreamId = transportStreamId,
            LastAttemptedAt = UtcTimes.Required(lastAttemptedAt, nameof(lastAttemptedAt)),
            LastCompletedAt = UtcTimes.Optional(lastCompletedAt, nameof(lastCompletedAt)),
            Outcome = outcome,
            ConsecutiveIncomplete = consecutiveIncomplete,
            LastDurationMilliseconds = lastDurationMilliseconds,
        };
    }

    public static StreamVisit Record(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        VisitOutcome outcome,
        DateTime at,
        TimeSpan took)
        => Rehydrate(
            networkId,
            transportStreamId,
            at,
            Settles(outcome) ? at : null,
            outcome,
            Counts(outcome) ? 1 : 0,
            Milliseconds(took));

    public void Record(VisitOutcome outcome, DateTime at, TimeSpan took)
    {
        LastAttemptedAt = UtcTimes.Required(at, nameof(at));
        LastDurationMilliseconds = Milliseconds(took);
        Outcome = outcome;

        if (Settles(outcome))
        {
            LastCompletedAt = at;
        }

        if (Counts(outcome))
        {
            ConsecutiveIncomplete++;
        }
        else if (Settles(outcome))
        {
            ConsecutiveIncomplete = 0;
        }
    }

    private static bool Settles(VisitOutcome outcome)
        => outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly;

    private static bool Counts(VisitOutcome outcome)
        => outcome is VisitOutcome.Incomplete or VisitOutcome.NoLock or VisitOutcome.NoBytes;

    private static int Milliseconds(TimeSpan took)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(took.Ticks, nameof(took));

        return (int)Math.Min(took.TotalMilliseconds, int.MaxValue);
    }
}
