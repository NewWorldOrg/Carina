using Carina.Contracts;
using Carina.Infrastructure.Scanning;

namespace Carina.Api.Responder.Scans;

public sealed record ScanStartedResponder(Guid ScanId)
{
    public static ScanStartedResponder Of(ScanLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        return new ScanStartedResponder(launch.Started!.Value);
    }
}

public sealed record ScanRefusedResponder(Guid? RunningScanId)
{
    public static ScanRefusedResponder Of(ScanLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        return new ScanRefusedResponder(launch.AlreadyRunning?.Value);
    }
}

public sealed record ScanApplicationResponder(
    IReadOnlyList<TuneSystem> Systems,
    int ServicesAdded,
    int ServicesUpdated,
    int ServicesRemoved,
    int ChannelsAdded,
    int ChannelsRemoved)
{
    public static ScanApplicationResponder Of(ScanApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new ScanApplicationResponder(
            application.Systems,
            application.ServicesAdded,
            application.ServicesUpdated,
            application.ServicesRemoved,
            application.ChannelsAdded,
            application.ChannelsRemoved);
    }
}
