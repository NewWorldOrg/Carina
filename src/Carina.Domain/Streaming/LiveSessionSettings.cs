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
        long? mostBytesWaitingToBeFed = null,
        TimeSpan? longestWaitForAViewer = null)
    {
        TimeSpan outliving = linger ?? TimeSpan.FromSeconds(5);
        TimeSpan raise = longestRaise ?? TimeSpan.FromSeconds(30);
        TimeSpan ahead = heldAhead ?? TimeSpan.FromMinutes(10);
        TimeSpan holds = betweenHolds ?? TimeSpan.FromMinutes(1);
        TimeSpan mouthful = longestWaitToBeFed ?? TimeSpan.FromSeconds(10);
        TimeSpan coming = longestWaitForATunerToComeFree ?? TimeSpan.FromSeconds(5);
        long held = mostBytesWaitingToBeFed ?? 32L * 1024 * 1024;
        TimeSpan awaited = longestWaitForAViewer ?? TimeSpan.FromSeconds(8);

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

        if (awaited <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longestWaitForAViewer),
                awaited,
                "A session started ahead waits some time for its viewer, not none, or it is stopped before the viewer can connect.");
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
        LongestWaitForAViewer = awaited;
    }

    public TimeSpan Linger { get; }

    public TimeSpan LongestRaise { get; }

    public TimeSpan HeldAhead { get; }

    public TimeSpan BetweenHolds { get; }

    public TimeSpan LongestWaitToBeFed { get; }

    public long MostBytesWaitingToBeFed { get; }

    public TimeSpan LongestWaitForATunerToComeFree { get; }

    public TimeSpan LongestWaitForAViewer { get; }
}
