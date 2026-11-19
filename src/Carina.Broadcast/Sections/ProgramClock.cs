namespace Carina.Broadcast.Sections;

public sealed class ProgramClock
{
    public const long Modulus = 1L << 33;

    private const long HalfWay = Modulus / 2;

    public long? Now { get; private set; }

    public long Follow(long reading)
    {
        long followed = Now is { } before ? Nearest(reading, before) : Wrapped(reading);

        Now = followed;

        return followed;
    }

    public long? Place(long reading) => Now is { } now ? Nearest(reading, now) : null;

    private static long Nearest(long reading, long from)
    {
        long ahead = Wrapped(reading - from);

        return from + (ahead >= HalfWay ? ahead - Modulus : ahead);
    }

    private static long Wrapped(long reading) => ((reading % Modulus) + Modulus) % Modulus;
}
