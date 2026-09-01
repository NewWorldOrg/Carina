namespace Carina.Domain.Streaming;

public sealed record OnTheFlySettings
{
    public const int Fewest = 1;

    private readonly int atOnce = 2;

    private readonly TimeSpan longestWaitForTheFirstByte = TimeSpan.FromSeconds(30);

    public int AtOnce
    {
        get => atOnce;

        init => atOnce = value >= Fewest
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A ceiling of none is not a ceiling, it is a route that never plays.");
    }

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
