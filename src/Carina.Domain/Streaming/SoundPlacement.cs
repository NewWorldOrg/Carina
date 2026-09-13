namespace Carina.Domain.Streaming;

public enum SoundChannel
{
    Left = 1,

    Right = 2,
}

public sealed record SoundPlacement
{
    private SoundPlacement(int ordinal, SoundChannel? channel)
    {
        Ordinal = ordinal;
        Channel = channel;
    }

    public int Ordinal { get; }

    public SoundChannel? Channel { get; }

    public bool IsWholeStream => Channel is null;

    public bool IsAllOfTheFirstStream => Ordinal is 0 && Channel is null;

    public static SoundPlacement WholeStream(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        return new SoundPlacement(ordinal, null);
    }

    public static SoundPlacement OneChannelOf(int ordinal, SoundChannel channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "A sound carried on one channel of a stream is on the left of it or on the right of it.");
        }

        return new SoundPlacement(ordinal, channel);
    }
}
