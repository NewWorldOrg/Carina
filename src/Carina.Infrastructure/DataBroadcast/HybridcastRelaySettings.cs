using Carina.Broadcast.Hybridcast;

namespace Carina.Infrastructure.DataBroadcast;

public sealed record HybridcastRelaySettings(
    int AtOnce,
    TimeSpan Time,
    int LargestSent,
    int LargestAit,
    int LargestReceived,
    int Redirects,
    int SocketsAtOnce,
    int LargestMessage,
    string UserAgent)
{
    public static HybridcastRelaySettings Default { get; } = new(
        AtOnce: 6,
        Time: TimeSpan.FromSeconds(15),
        LargestSent: BroadcasterRelaySettings.Default.LargestSent,
        LargestAit: XmlAit.Largest,
        LargestReceived: 4 * 1024 * 1024,
        Redirects: 3,
        SocketsAtOnce: 4,
        LargestMessage: 1024 * 1024,
        UserAgent: BroadcasterRelaySettings.Default.UserAgent);
}
