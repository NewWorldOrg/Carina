using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverStartupTests
{
    private static readonly string[] TwoProblems =
    [
        "socketPath: expected an absolute path, got 'driver.sock'.",
        "devices: expected at least one device.",
    ];

    [Fact]
    public void AUsableConfigurationStartsTheProcess()
    {
        var writer = new StringWriter();

        var exitCode = DriverStartup.Report(
            DriverConfigurationResult.Usable(
                new DriverConfiguration(
                    "/run/carina/driver.sock",
                    [new OutputRootSettings("primary", "/srv/recordings")],
                    6,
                    new TunerSettings(TunerBackend.Fake),
                    [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
                )
            ),
            writer
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ABadConfigurationStopsTheProcess()
    {
        var writer = new StringWriter();

        var exitCode = DriverStartup.Report(
            DriverConfigurationResult.Unusable(TwoProblems),
            writer
        );

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void EveryFindingIsPrinted()
    {
        var writer = new StringWriter();

        DriverStartup.Report(DriverConfigurationResult.Unusable(TwoProblems), writer);

        var written = writer.ToString();
        Assert.All(TwoProblems, problem => Assert.Contains(problem, written));
    }

    [Fact]
    public void AStopThatWasAskedForIsASuccess()
    {
        Assert.Equal(0, DriverStartup.ExitCodeFor(stopWasAsked: true));
    }

    [Fact]
    public void AHostThatStoppedByItselfIsAFailure()
    {
        Assert.NotEqual(0, DriverStartup.ExitCodeFor(stopWasAsked: false));
        Assert.Equal(
            DriverStartup.StoppedEarlyExitCode,
            DriverStartup.ExitCodeFor(stopWasAsked: false)
        );
    }

    [Fact]
    public void TheOutputSaysWhichFileItWasReading()
    {
        var writer = new StringWriter();

        DriverStartup.Report(
            DriverConfigurationResult.Unusable(TwoProblems),
            writer,
            "/etc/carina/driver.json"
        );

        Assert.Contains("/etc/carina/driver.json", writer.ToString());
    }

    [Fact]
    public void TheOutputNamesTheVariableWhenThereIsNoPath()
    {
        var writer = new StringWriter();

        DriverStartup.Report(DriverConfigurationResult.Unusable(TwoProblems), writer);

        Assert.Contains(DriverStartup.ConfigurationPathVariable, writer.ToString());
    }
}
