namespace Carina.Driver.Configuration;

public static class DriverStartup
{
    public const string ConfigurationPathVariable = "CARINA_DRIVER_CONFIG";

    public const int ConfigurationExitCode = 78;

    public static int Report(
        DriverConfigurationResult result,
        TextWriter error,
        string? path = null
    )
    {
        if (result.Configuration is not null)
        {
            return 0;
        }

        error.WriteLine(
            path is null
                ? "The driver configuration is not usable:"
                : $"The driver configuration at '{path}' is not usable:"
        );

        foreach (var problem in result.Problems)
        {
            error.WriteLine($"  {problem}");
        }

        error.WriteLine(
            "Nothing was opened and no socket was bound. Fix the settings above and start again."
        );

        return ConfigurationExitCode;
    }
}
