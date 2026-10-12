using Carina.Domain.Auth;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// A Hybridcast application started on a live channel for one viewer: the channel, who started it, the address it
/// was entered at and the boundary its relay keeps to.
/// </summary>
public sealed record HybridcastLaunch(string Channel, Subject Subject, Uri EntryUrl, HybridcastBoundary Boundary);

/// <summary>
/// The applications started on live channels, each found again by the ticket the application's frame carries.
/// </summary>
public interface IHybridcastLaunches
{
    void Keep(string carrier, HybridcastLaunch launch);

    HybridcastLaunch? Find(string? carrier);
}
