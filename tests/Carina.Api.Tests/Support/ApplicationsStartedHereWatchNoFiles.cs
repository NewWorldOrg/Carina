using System.Runtime.CompilerServices;

namespace Carina.Api.Tests.Support;

internal static class ApplicationsStartedHereWatchNoFiles
{
    [ModuleInitializer]
    internal static void Initialize()
        => Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
