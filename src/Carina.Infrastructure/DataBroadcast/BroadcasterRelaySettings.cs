using System.Reflection;

namespace Carina.Infrastructure.DataBroadcast;

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
