namespace Carina.Domain.DataBroadcast;

public static class StreamClock
{
    public const int Hertz = 90_000;

    public static TimeSpan ToTime(long ticks)
        => TimeSpan.FromTicks((long)((Int128)ticks * TimeSpan.TicksPerSecond / Hertz));
}
