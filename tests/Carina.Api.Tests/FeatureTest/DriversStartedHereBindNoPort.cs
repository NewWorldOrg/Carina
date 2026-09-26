extern alias driver;

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

using driver::Carina.Driver.Ipc;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// Clears, once for the whole test process, the environment variables that would have a driver
/// started here bind a TCP port.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class DriversStartedHereBindNoPort
{
    private static readonly string[] SettingsThatWouldBindAPort =
    [
        .. TcpBindingGate.Variables,
        "DOTNET_URLS",
        "URLS",
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        foreach (string name in SettingsThatWouldBindAPort)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
