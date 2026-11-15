using System.Runtime.CompilerServices;

namespace Carina.Api.Tests.Support;

internal static class ApplicationsStartedHereWatchNoFiles
{
    // Every application started here otherwise takes an inotify instance to watch its configuration file, and holds it until the test process ends.
    [ModuleInitializer]
    internal static void Initialize()
        => Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
