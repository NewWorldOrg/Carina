namespace Carina.Domain.Streaming;

public sealed record LiveSessionSettings
{
    private readonly TimeSpan linger = TimeSpan.FromSeconds(5);

    private readonly TimeSpan longestRaise = TimeSpan.FromSeconds(30);

    private readonly TimeSpan longestWaitToBeFed = TimeSpan.FromSeconds(10);

    public TimeSpan Linger
    {
        get => linger;

        init => linger = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A session outlives its last viewer for some time, not none, or every reload pays the whole start again.");
    }

    public TimeSpan LongestRaise
    {
        get => longestRaise;

        init => longestRaise = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A viewer waits to be seated for some time, not none, or no channel could ever be raised.");
    }

    /// <summary>
    /// How long one transcoder may keep the reading of the channel waiting before it is cut loose.
    /// </summary>
    /// <remarks>
    /// Bytes into a transcoder cannot be dropped the way frames to a viewer can, so a transcoder
    /// that has stopped reading is let go of rather than waited for: the others are watching the
    /// same channel through the same reading.
    /// </remarks>
    public TimeSpan LongestWaitToBeFed
    {
        get => longestWaitToBeFed;

        init => longestWaitToBeFed = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A transcoder is given some time to take a mouthful, not none, or the first one is cut.");
    }
}
