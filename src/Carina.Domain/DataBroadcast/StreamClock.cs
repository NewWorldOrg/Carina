namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The clock every time of a data broadcast is told on: 90 kHz from the stream's presentation clock with its
/// wrap undone, so the times of a recording are the recording's own and never a raw PTS that comes around.
/// </summary>
public static class StreamClock
{
    public const int Hertz = 90_000;

    public static TimeSpan ToTime(long ticks)
        => TimeSpan.FromTicks((long)((Int128)ticks * TimeSpan.TicksPerSecond / Hertz));
}
