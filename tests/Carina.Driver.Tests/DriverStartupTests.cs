using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

/// <summary>
/// What the process does with a configuration it cannot use.
/// </summary>
/// <remarks>
/// The decision belongs before anything is opened: a driver that binds its socket
/// and then discovers a bad device path has already told the app it is available.
/// So the answer is an exit code and a list of findings on standard error, which is
/// all an operator has when a container refuses to come up.
/// </remarks>
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
            new DriverConfigurationResult(
                new DriverConfiguration(
                    "/run/carina/driver.sock",
                    "/srv/recordings",
                    6,
                    new TunerSettings(TunerBackend.Fake),
                    [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
                ),
                []
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
            new DriverConfigurationResult(null, TwoProblems),
            writer
        );

        Assert.NotEqual(0, exitCode);
    }

    // Every finding, not the first: otherwise the operator fixes one line, restarts,
    // and learns about the next one.
    [Fact]
    public void EveryFindingIsPrinted()
    {
        var writer = new StringWriter();

        DriverStartup.Report(new DriverConfigurationResult(null, TwoProblems), writer);

        var written = writer.ToString();
        Assert.All(TwoProblems, problem => Assert.Contains(problem, written));
    }

    [Fact]
    public void TheOutputSaysWhichFileItWasReading()
    {
        var writer = new StringWriter();

        DriverStartup.Report(
            new DriverConfigurationResult(null, TwoProblems),
            writer,
            "/etc/carina/driver.json"
        );

        Assert.Contains("/etc/carina/driver.json", writer.ToString());
    }
}
