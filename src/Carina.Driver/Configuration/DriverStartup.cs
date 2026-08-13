namespace Carina.Driver.Configuration;

public static class DriverStartup
{
    public const string ConfigurationPathVariable = "CARINA_DRIVER_CONFIG";

    public const int ConfigurationExitCode = 78;

    public const int StoppedEarlyExitCode = 70;

    public static int Report(
        DriverConfigurationResult result,
        TextWriter error,
        string? path = null
    )
    {
        if (result.TryGetConfiguration(out _, out var problems))
        {
            return 0;
        }

        error.WriteLine(
            path is null
                ? $"{ConfigurationPathVariable} names no configuration file:"
                : $"The driver configuration at '{path}' is not usable:"
        );

        foreach (var problem in problems)
        {
            error.WriteLine($"  {problem}");
        }

        error.WriteLine(
            "Nothing was opened and no socket was bound. Fix the settings above and start again."
        );

        return ConfigurationExitCode;
    }

    public static int ExitCodeFor(bool stopWasAsked) =>
        stopWasAsked ? 0 : StoppedEarlyExitCode;
}
