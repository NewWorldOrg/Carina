using Carina.Api.Services;
using Carina.Domain.DataBroadcast;

namespace Carina.Api.Responder.DataBroadcast;

/// <summary>
/// The applications an AIT names that this receiver can run.
/// </summary>
public sealed record HybridcastAitResponder(IReadOnlyList<HybridcastApplicationResponder> Applications)
{
    public static HybridcastAitResponder Of(IReadOnlyList<HybridcastApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        return new HybridcastAitResponder([.. applications.Select(HybridcastApplicationResponder.Of)]);
    }
}

/// <summary>
/// One application an AIT names: <c>orgId</c> and <c>appId</c> as numbers, <c>controlCode</c> numbered as the AIT
/// numbers it (1 AUTOSTART, 2 PRESENT, 4 KILL), the address it is entered at, the profile it declares where it
/// declares one, and the pairs of its boundary, none where the AIT carries no boundary at all.
/// </summary>
public sealed record HybridcastApplicationResponder(
    long OrgId,
    int AppId,
    int ControlCode,
    string EntryUrl,
    int? Profile,
    IReadOnlyList<HybridcastBoundaryResponder> Boundary)
{
    public static HybridcastApplicationResponder Of(HybridcastApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new HybridcastApplicationResponder(
            application.OrgId,
            application.AppId,
            (int)application.ControlCode,
            application.EntryUrl.AbsoluteUri,
            application.Profile,
            [.. application.Boundary.Select(pair => new HybridcastBoundaryResponder(pair.Bitmap, pair.ManagedUrls))]);
    }
}

/// <summary>
/// One pair of an application's boundary: the sixteen bits of what it is permitted, and the addresses the pair
/// applies to, none meaning every address.
/// </summary>
public sealed record HybridcastBoundaryResponder(int PermissionBitmap, IReadOnlyList<string> ManagedUrls);

/// <summary>
/// A Hybridcast application started for this viewer: the ticket its frame opens the relay with and when it lapses
/// unused, the address the application is entered at, and <c>address</c>, the path on this server the frame opens to
/// be handed that entry through the relay.
/// </summary>
public sealed record HybridcastTicketResponder(string InTheClear, DateTime LapsesAt, string EntryUrl, string Address)
{
    public static HybridcastTicketResponder Of(HybridcastStart started, string address)
    {
        ArgumentNullException.ThrowIfNull(started);

        return new HybridcastTicketResponder(started.Ticket.InTheClear, started.Ticket.LapsesAt, started.Application.EntryUrl.AbsoluteUri, address);
    }

    public override string ToString() => "a started Hybridcast application";
}
