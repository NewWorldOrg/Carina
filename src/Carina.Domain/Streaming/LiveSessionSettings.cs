namespace Carina.Domain.Streaming;

public sealed record LiveSessionSettings
{
    public LiveSessionSettings(
        TimeSpan? linger = null,
        TimeSpan? longestRaise = null,
        TimeSpan? heldAhead = null,
        TimeSpan? betweenHolds = null,
        TimeSpan? longestWaitToBeFed = null,
        TimeSpan? longestWaitForATunerToComeFree = null,
        long? mostBytesWaitingToBeFed = null)
    {
        TimeSpan outliving = linger ?? TimeSpan.FromSeconds(5);
        TimeSpan raise = longestRaise ?? TimeSpan.FromSeconds(30);
        TimeSpan ahead = heldAhead ?? TimeSpan.FromMinutes(10);
        TimeSpan holds = betweenHolds ?? TimeSpan.FromMinutes(1);
        TimeSpan mouthful = longestWaitToBeFed ?? TimeSpan.FromSeconds(10);
        TimeSpan coming = longestWaitForATunerToComeFree ?? TimeSpan.FromSeconds(5);
        long held = mostBytesWaitingToBeFed ?? 32L * 1024 * 1024;

        if (outliving <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(linger),
                outliving,
                "A session outlives its last viewer for some time, not none, or every reload pays the whole start again.");
        }

        if (raise <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longestRaise),
                raise,
                "A viewer waits to be seated for some time, not none, or no channel could ever be raised.");
        }

        if (ahead <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heldAhead),
                ahead,
                "A supply is held open for some time beyond now, not none, or it is let go of the moment it is asked for.");
        }

        if (holds <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenHolds),
                holds,
                "A supply is asked to be held open every so often, and every so often is a span, not none.");
        }

        if (mouthful <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longestWaitToBeFed),
                mouthful,
                "A transcoder is given some time to take a mouthful, not none, or the first one is cut.");
        }

        if (coming <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longestWaitForATunerToComeFree),
                coming,
                "A viewer waits some time for a tuner on its way out, not none, or a channel change lands in the gap.");
        }

        if (held <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostBytesWaitingToBeFed),
                held,
                "A transcoder is held some bytes it has not taken yet, not none, or the first mouthful is cut.");
        }

        if (holds >= ahead)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenHolds),
                holds,
                "A supply asked to be held open again only once what was asked for has run out is let go of while it is still being watched.");
        }

        Linger = outliving;
        LongestRaise = raise;
        HeldAhead = ahead;
        BetweenHolds = holds;
        LongestWaitToBeFed = mouthful;
        LongestWaitForATunerToComeFree = coming;
        MostBytesWaitingToBeFed = held;
    }

    public TimeSpan Linger { get; }

    public TimeSpan LongestRaise { get; }

    /// <summary>
    /// How far ahead of now the supply is asked to be held open while it is being watched.
    /// </summary>
    /// <remarks>
    /// This is what a viewing that is still there is worth once nothing more is heard from it: the
    /// driver lets go of a supply this long after the last time it was asked to hold on to it.
    /// </remarks>
    public TimeSpan HeldAhead { get; }

    /// <summary>
    /// How often the supply is asked to be held open for longer.
    /// </summary>
    public TimeSpan BetweenHolds { get; }

    /// <summary>
    /// How long the oldest bytes a transcoder has not taken yet may wait before it is cut loose.
    /// </summary>
    /// <remarks>
    /// Bytes into a transcoder cannot be dropped the way frames to a viewer can, so a transcoder
    /// that has stopped reading is let go of rather than waited for: the others are watching the
    /// same channel through the same reading.
    /// </remarks>
    public TimeSpan LongestWaitToBeFed { get; }

    /// <summary>
    /// How many bytes a transcoder may have not taken yet before it is cut loose.
    /// </summary>
    public long MostBytesWaitingToBeFed { get; }

    /// <summary>
    /// How long a viewer refused for want of a tuner waits for one that is already being let go of.
    /// </summary>
    /// <remarks>
    /// A session leaves the ledger when it is closed and lets the tuner go at the end of its
    /// teardown, so a viewer arriving between the two finds nothing to give up and a tuner that is
    /// not free yet. It waits that teardown out rather than being refused, and no longer than this:
    /// a teardown that will not end is a tuner that never comes free, and the refusal stands.
    /// </remarks>
    public TimeSpan LongestWaitForATunerToComeFree { get; }
}
