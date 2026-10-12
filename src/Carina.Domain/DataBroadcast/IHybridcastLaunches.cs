using Carina.Domain.Auth;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// A Hybridcast application started on a live channel for one viewer: the channel, who started it, the address it
/// was entered at, the boundary its relay keeps to, and the path of the receiver script its documents are given.
/// </summary>
public sealed record HybridcastLaunch(string Channel, Subject Subject, Uri EntryUrl, HybridcastBoundary Boundary, string ReceiverScript);

/// <summary>
/// The applications started on live channels, each found again by the ticket the application's frame carries.
/// </summary>
public interface IHybridcastLaunches
{
    void Keep(string carrier, HybridcastLaunch launch);

    HybridcastLaunch? Find(string? carrier);
}
