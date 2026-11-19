using Carina.Domain.Auth;

namespace Carina.Domain.DataBroadcast;

public sealed record HybridcastLaunch(string Channel, Subject Subject, Uri EntryUrl, HybridcastBoundary Boundary, string ReceiverScript);

public interface IHybridcastLaunches
{
    void Keep(string carrier, HybridcastLaunch launch);

    HybridcastLaunch? Find(string? carrier);
}
