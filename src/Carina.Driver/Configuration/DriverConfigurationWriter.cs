using System.Text.Json;

namespace Carina.Driver.Configuration;

public static class DriverConfigurationWriter
{
    public static string Serialize(DriverConfiguration configuration) =>
        JsonSerializer.Serialize(
            configuration,
            DriverConfigurationJsonContext.Default.DriverConfiguration
        );
}
