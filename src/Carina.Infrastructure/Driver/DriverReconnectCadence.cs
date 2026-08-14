namespace Carina.Infrastructure.Driver;

public sealed class DriverReconnectCadence
{
    private readonly ReconnectBackoff reconnect;
    private readonly ReconnectBackoff drainPoll;
    private readonly TimeSpan minimumFeedDwell;

    public DriverReconnectCadence(DriverSupervisionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MinimumFeedDwell, TimeSpan.Zero);

        reconnect = new ReconnectBackoff(settings.FirstDelay, settings.DelayCap, settings.Chance);
        drainPoll = new ReconnectBackoff(settings.DrainPoll, settings.DrainPoll, settings.Chance);
        minimumFeedDwell = settings.MinimumFeedDwell;
    }

    public TimeSpan AfterSetback() => reconnect.Next();

    public TimeSpan AfterFeed(TimeSpan held)
    {
        if (held >= minimumFeedDwell)
        {
            reconnect.Reset();
        }

        return reconnect.Next();
    }

    public TimeSpan WhileDraining() => drainPoll.Next();
}
