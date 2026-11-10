using Carina.Api.Common;
using Carina.Domain.Notifications;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationWatchResponder(
    Guid Id,
    string ProgrammeId,
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTime StartsAt,
    DateTime EndsAt,
    string ProgrammeName,
    string? StationName,
    int LeadMinutes,
    IReadOnlyList<Guid> Destinations,
    ProgrammeWatchState State,
    DateTime? NotifiedAt,
    DateTime CreatedAt,
    DateTime ChangedAt)
{
    public static NotificationWatchResponder Of(ProgrammeWatch watch)
    {
        ArgumentNullException.ThrowIfNull(watch);

        return new NotificationWatchResponder(
            watch.Id.Value,
            ProgrammeIdText.Of(watch.Programme),
            watch.NetworkId.Value,
            watch.ServiceId.Value,
            watch.EventId.Value,
            watch.StartsAt,
            watch.EndsAt,
            watch.ProgrammeName,
            watch.StationName,
            watch.LeadMinutes,
            [.. watch.Destinations.Select(destination => destination.Value)],
            watch.State,
            watch.NotifiedAt,
            watch.CreatedAt,
            watch.ChangedAt);
    }
}

public sealed record NotificationWatchListResponder(IReadOnlyList<NotificationWatchResponder> Items)
{
    public static NotificationWatchListResponder Of(IReadOnlyList<ProgrammeWatch> watches)
    {
        ArgumentNullException.ThrowIfNull(watches);

        return new NotificationWatchListResponder([.. watches.Select(NotificationWatchResponder.Of)]);
    }
}
