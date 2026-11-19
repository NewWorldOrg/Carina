using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

public sealed record WatchedBroadcast(DateTime StartsAt, DateTime EndsAt, string ProgrammeName, string? StationName)
{
    public static WatchedBroadcast Of(Programme announced, string? stationName)
    {
        ArgumentNullException.ThrowIfNull(announced);

        return new WatchedBroadcast(announced.StartsAt, EpgComparison.EndOf(announced), announced.Name, stationName);
    }
}
