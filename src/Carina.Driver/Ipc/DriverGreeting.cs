using Carina.Contracts;

namespace Carina.Driver.Ipc;

public static class DriverGreeting
{
    public static readonly IReadOnlyList<string> Capabilities =
    [
        DriverCapabilities.Recording,
        DriverCapabilities.Live,
        DriverCapabilities.QualityMetering,
        DriverCapabilities.LiveTunerToggle,
    ];

    public static DriverHello ForThisProcess() =>
        new(DriverProtocol.Version, Guid.NewGuid().ToString("N"), Capabilities);
}
