using System.Reflection;

namespace Carina.Infrastructure.Tests.Streaming;

public static class Probes
{
    public const string BroadcastHd = "broadcast-hd";
    public const string BroadcastSd = "broadcast-sd";
    public const string Progressive = "progressive";
    public const string Multiplex = "multiplex";
    public const string Mono = "audio-mono";
    public const string Surround = "audio-surround";
    public const string UndeterminedLayout = "audio-undetermined-layout";
    public const string Refused = "refused";

    public static string Recorded(string name)
    {
        using Stream? held = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream($"Carina.Infrastructure.Tests.Streaming.Probes.{name}.txt");

        Assert.NotNull(held);

        using var reading = new StreamReader(held);

        return reading.ReadToEnd();
    }
}
