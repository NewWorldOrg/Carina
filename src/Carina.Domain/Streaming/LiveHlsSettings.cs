namespace Carina.Domain.Streaming;

/// <summary>
/// How a live channel is handed to a player that opens a playlist: how long a key outlives the last request made with
/// it, how many segments are held, where a segment may close, and how many keys are held at once.
/// </summary>
public sealed record LiveHlsSettings
{
    private readonly TimeSpan idle = TimeSpan.FromSeconds(30);

    private readonly int segmentsHeld = 6;

    private readonly TimeSpan shortestSegment = TimeSpan.FromSeconds(1.5);

    private readonly TimeSpan longestWait = new LiveSessionSettings().LongestRaise;

    private readonly int mostHeldAtOnce = 32;

    private readonly int mostHeldPerWatcher = 8;

    public TimeSpan Idle
    {
        get => idle;

        init => idle = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A key outlives the last request made with it by some time, not none, or no second request finds it.");
    }

    public int SegmentsHeld
    {
        get => segmentsHeld;

        init => segmentsHeld = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A playlist lists some segment, not none, or a player has nothing to fetch.");
    }

    /// <summary>
    /// How long a segment runs before the next keyframe may close it.
    /// </summary>
    public TimeSpan ShortestSegment
    {
        get => shortestSegment;

        init => shortestSegment = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A segment runs for some time, not none.");
    }

    /// <summary>
    /// How long the first playlist waits for the first segment.
    /// </summary>
    public TimeSpan LongestWait
    {
        get => longestWait;

        init => longestWait = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The first segment is waited for some time, not none, or no channel could ever be opened.");
    }

    public int MostHeldAtOnce
    {
        get => mostHeldAtOnce;

        init => mostHeldAtOnce = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Some key is held, not none, or no player could open a playlist.");
    }

    public int MostHeldPerWatcher
    {
        get => mostHeldPerWatcher;

        init => mostHeldPerWatcher = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A watcher is held some key, not none, or nobody could open a playlist.");
    }
}
