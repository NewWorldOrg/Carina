using Carina.Api.Services;
using Carina.Domain.DataBroadcast;

namespace Carina.Api.Responder.DataBroadcast;

public sealed record HybridcastAitResponder(IReadOnlyList<HybridcastApplicationResponder> Applications)
{
    public static HybridcastAitResponder Of(IReadOnlyList<HybridcastApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        return new HybridcastAitResponder([.. applications.Select(HybridcastApplicationResponder.Of)]);
    }
}

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

public sealed record HybridcastBoundaryResponder(int PermissionBitmap, IReadOnlyList<string> ManagedUrls);

public sealed record HybridcastTicketResponder(string InTheClear, DateTime LapsesAt, string EntryUrl, string Address)
{
    public static HybridcastTicketResponder Of(HybridcastStart started, string address)
    {
        ArgumentNullException.ThrowIfNull(started);

        return new HybridcastTicketResponder(started.Ticket.InTheClear, started.Ticket.LapsesAt, started.Application.EntryUrl.AbsoluteUri, address);
    }

    public override string ToString() => "a started Hybridcast application";
}
