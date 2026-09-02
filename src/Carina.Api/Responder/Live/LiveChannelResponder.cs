using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveChannelResponder(
    int NetworkId,
    int ServiceId,
    string Name,
    ServiceCategory Category,
    int? RemoteControlKeyId,
    int Viewers,
    ScanTargetResponder? Tuning,
    IReadOnlyList<LiveSessionResponder>? Sessions)
{
    public static LiveChannelResponder Of(LiveChannelListing listing, LiveChannelQuery query)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(query);

        return new LiveChannelResponder(
            listing.Service.NetworkId.Value,
            listing.Service.ServiceId.Value,
            listing.Service.Name,
            listing.Service.Category,
            listing.Service.RemoteControlKeyId,
            listing.Viewers,
            query.Asks(LiveChannelField.Tuning) ? ScanTargetResponder.Of(listing.Selected.Tuning) : null,
            query.Asks(LiveChannelField.Sessions) ? [.. listing.Sessions.Select(LiveSessionResponder.Of)] : null);
    }
}

public sealed record LiveChannelListResponder(
    IReadOnlyList<LiveChannelResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static LiveChannelListResponder Of(PaginatedList<LiveChannelListing> found, LiveChannelQuery query)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new LiveChannelListResponder(
            [.. found.Items.Select(listing => LiveChannelResponder.Of(listing, query))],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
