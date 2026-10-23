using Carina.Domain.DataBroadcast;

namespace Carina.Api.Responder.DataBroadcast;

/// <summary>
/// Where the receiver is, as data broadcasting reads it: the postal code as seven digits and the prefecture as its
/// place in the prefecture designation of ARIB STD-B10 annex G, table G-2, counted from 0, each null while it is not
/// registered, with when they last changed, null until somebody changes them.
/// </summary>
public sealed record DataBroadcastSettingsResponder(string? PostalCode, int? Prefecture, DateTime? ChangedAt)
{
    public static DataBroadcastSettingsResponder Of(DataBroadcastSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new DataBroadcastSettingsResponder(standing.PostalCode, standing.Prefecture, standing.ChangedAt);
    }
}
