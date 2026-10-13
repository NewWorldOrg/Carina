using Carina.Broadcast.Hybridcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// How far the relay goes for a Hybridcast application: how many exchanges a channel has under way at once and how
/// long one may take including the wait for its turn, the most an application posts, which is what a data broadcast
/// document sends, the most it takes back for an AIT and for any other document or resource, how many redirects
/// inside the boundary it follows, how many WebSockets a channel holds open at once
/// and the largest message one carries, and the name it introduces itself by, which is the data broadcast relay's.
/// </summary>
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
