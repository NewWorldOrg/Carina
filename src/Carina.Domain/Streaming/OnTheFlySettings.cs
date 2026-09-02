namespace Carina.Domain.Streaming;

public sealed record OnTheFlySettings
{
    private readonly TimeSpan longestWaitForTheFirstByte = TimeSpan.FromSeconds(30);

    public TimeSpan LongestWaitForTheFirstByte
    {
        get => longestWaitForTheFirstByte;

        init => longestWaitForTheFirstByte = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A transcoder is given some time to produce its first byte, not none.");
    }
}
