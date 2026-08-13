namespace Carina.Driver.Configuration;

/// <summary>
/// Turns the outcome of reading the configuration into what the process does next.
/// </summary>
/// <remarks>
/// This runs before the socket is bound and before a device is opened. A driver
/// that started first and validated later would have already told the app it was
/// available, and would then hold a tuner it cannot use.
/// </remarks>
public static class DriverStartup
{
    /// <summary>The environment variable naming the configuration file.</summary>
    public const string ConfigurationPathVariable = "CARINA_DRIVER_CONFIG";

    /// <summary>The exit code for a configuration the driver cannot start with.</summary>
    public const int ConfigurationExitCode = 78;

    /// <summary>
    /// Writes the findings, if any, and says what the process should exit with.
    /// </summary>
    /// <param name="result">What reading the configuration produced.</param>
    /// <param name="error">Where findings go; standard error in the running process.</param>
    /// <param name="path">The file that was read, named so the operator can find it.</param>
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
