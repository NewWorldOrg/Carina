namespace Carina.Domain.Streaming;

public sealed record LiveSessionSettings
{
    private readonly TimeSpan linger = TimeSpan.FromSeconds(5);

    private readonly TimeSpan longestRaise = TimeSpan.FromSeconds(30);

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
}
