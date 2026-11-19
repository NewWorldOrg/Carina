using Carina.Domain.DataBroadcast;

namespace Carina.Api.Responder.DataBroadcast;

public sealed record DataBroadcastSettingsResponder(string? PostalCode, int? Prefecture, DateTime? ChangedAt)
{
    public static DataBroadcastSettingsResponder Of(DataBroadcastSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new DataBroadcastSettingsResponder(standing.PostalCode, standing.Prefecture, standing.ChangedAt);
    }
}
