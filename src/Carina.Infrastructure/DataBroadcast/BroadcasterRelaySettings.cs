using System.Reflection;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// How far the relay goes for a document: how many exchanges a channel has under way at once, how long one may
/// take including the wait for its turn, the most it sends and the most it takes back, how many redirects on the
/// same host it follows, and the name it introduces itself by.
/// </summary>
public sealed record BroadcasterRelaySettings(
    int AtOnce,
    TimeSpan Time,
    int LargestSent,
    int LargestReceived,
    int Redirects,
    string UserAgent)
{
    public static BroadcasterRelaySettings Default { get; } = new(
        AtOnce: 2,
        Time: TimeSpan.FromSeconds(15),
        LargestSent: 16 * 1024,
        LargestReceived: 256 * 1024,
        Redirects: 3,
        UserAgent: $"Carina/{VersionOf(typeof(BroadcasterRelaySettings).Assembly)}");

    private static string VersionOf(Assembly assembly)
        => (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0")
            .Split('+')[0];
}
