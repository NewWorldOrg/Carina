namespace Carina.Domain.Streaming;

public sealed record LiveFanoutSettings
{
    private readonly int longestBacklog = 15;

    public int LongestBacklog
    {
        get => longestBacklog;

        init => longestBacklog = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A viewer is allowed to fall some way behind before its pictures are thrown away, not none.");
    }
}
