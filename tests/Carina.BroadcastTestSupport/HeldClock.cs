using Carina.Broadcast.Tables;

namespace Carina.BroadcastTestSupport;

public sealed class HeldClock(DateTimeOffset held) : TimeProvider
{
    private DateTimeOffset now = held;

    public static HeldClock Broadcasting(int year, int month, int day, int hour, int minute, int second)
        => new(new DateTimeOffset(year, month, day, hour, minute, second, BroadcastTime.Offset));

    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public void MoveOn(TimeSpan by) => now += by;
}
