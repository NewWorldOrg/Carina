namespace Carina.Broadcast.Sections;

/// <summary>
/// The 90 kHz clock of a programme followed through its 33-bit wrap: the first reading is taken as it is,
/// and every later one is placed at the nearest moment to the one before it.
/// </summary>
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

    /// <summary>
    /// Places a 33-bit reading at the moment nearest to where the clock is now, without moving the clock.
    /// </summary>
    public long? Place(long reading) => Now is { } now ? Nearest(reading, now) : null;

    private static long Nearest(long reading, long from)
    {
        long ahead = Wrapped(reading - from);

        return from + (ahead >= HalfWay ? ahead - Modulus : ahead);
    }

    private static long Wrapped(long reading) => ((reading % Modulus) + Modulus) % Modulus;
}
