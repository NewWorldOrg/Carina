using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

/// <summary>
/// What the guide says, at one look, of a programme asked to be told of.
/// </summary>
public sealed record WatchedGuide(GuideStanding Standing, WatchedBroadcast? Announced)
{
    public static WatchedGuide Of(Programme? announced, DateTime? heardWholeAt, string? stationName)
    {
        GuideStanding standing = GuideReading.Of(announced, heardWholeAt);

        return standing is GuideStanding.Announced && announced is not null
            ? new WatchedGuide(standing, WatchedBroadcast.Of(announced, stationName))
            : new WatchedGuide(standing, null);
    }
}
