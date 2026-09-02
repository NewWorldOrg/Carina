using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveStartupMarkResponder(LiveStartupSegment Segment, long? ReachedAtMs, long? TookMs)
{
    public static LiveStartupMarkResponder Of(LiveStartupMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);

        return new LiveStartupMarkResponder(
            mark.Segment,
            mark.ReachedAt is { } reached ? (long)reached.TotalMilliseconds : null,
            mark.Took is { } took ? (long)took.TotalMilliseconds : null);
    }
}

public sealed record LiveStartupResponder(bool InProgress, IReadOnlyList<LiveStartupMarkResponder> Marks)
{
    public static LiveStartupResponder Of(LiveStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        return new LiveStartupResponder(startup.InProgress, [.. startup.Timeline.Select(LiveStartupMarkResponder.Of)]);
    }
}

public sealed record LiveSessionResponder(
    int NetworkId,
    int ServiceId,
    string Profile,
    int Viewers,
    long Dropped,
    LiveStartupResponder Startup)
{
    public static LiveSessionResponder Of(LiveSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new LiveSessionResponder(
            session.Key.Network.Value,
            session.Key.Service.Value,
            session.Key.Profile.Name,
            session.Viewers,
            session.Dropped,
            LiveStartupResponder.Of(session.Startup));
    }
}
