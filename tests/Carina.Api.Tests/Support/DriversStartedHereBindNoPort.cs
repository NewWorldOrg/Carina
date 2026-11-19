extern alias driver;

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

using driver::Puppis.Driver.Ipc;

namespace Carina.Api.Tests.Support;

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
