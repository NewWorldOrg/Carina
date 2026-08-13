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

    public static int ReportUnusableConfiguration(
        IReadOnlyList<string> problems,
        TextWriter error
    )
    {
        error.WriteLine("The driver may not serve what this configuration asks of it:");

        foreach (var problem in problems)
        {
            error.WriteLine($"  {problem}");
        }

        error.WriteLine("No socket was bound and no device was opened.");

        return ConfigurationExitCode;
    }

    public static int ReportUnusableSocket(IReadOnlyList<string> problems, TextWriter error)
    {
        error.WriteLine("The driver could not take the socket it answers on:");

        foreach (var problem in problems)
        {
            error.WriteLine($"  {problem}");
        }

        error.WriteLine("No socket was bound and no device was opened.");

        return StoppedEarlyExitCode;
    }

    public static int ReportFailure(Exception failure, TextWriter error)
    {
        error.WriteLine($"The driver stopped: {failure.Message}");

        return StoppedEarlyExitCode;
    }

    public static void Announce(DriverConfiguration configuration, TextWriter output)
    {
        output.WriteLine(DriverShutdownBudget.From(configuration).Describe());

        if (configuration.Tuner?.Backend is not TunerBackend.Fake)
        {
            return;
        }

        output.WriteLine(
            "tuner.backend = fake: this driver produces synthetic transport stream, not broadcast."
        );
        output.WriteLine(
            "Nothing recorded here is a television programme. Set tuner.backend to dvb for real tuners."
        );
    }

    public static int ExitCodeFor(bool stopWasAsked) =>
        stopWasAsked ? 0 : StoppedEarlyExitCode;
}
