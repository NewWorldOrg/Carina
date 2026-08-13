using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class DriverShutdownBudgetTests
{
    private static DriverConfiguration Configuration(int shutdownGraceHours) =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
            shutdownGraceHours,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial)]
        );

    [Fact]
    public void TheBudgetIsTheLingerCapPlusTheHardStopPlusTheHostSlack()
    {
        var budget = DriverShutdownBudget.From(Configuration(6));

        Assert.Equal(TimeSpan.FromHours(6), budget.Drain);
        Assert.Equal(TunerSessionManager.DefaultHardStopLimit, budget.HardStop);
        Assert.Equal(DriverShutdownBudget.DefaultHostSlack, budget.HostSlack);
        Assert.Equal(21600 + 30 + 60, budget.TotalSeconds);
    }

    [Fact]
    public void TheBudgetOutlivesTheLingerCapAlone()
    {
        var budget = DriverShutdownBudget.From(Configuration(6));

        Assert.True(budget.Total > budget.Drain);
    }

    [Fact]
    public void TheBudgetMatchesWhatTheSessionManagerWillWaitFor()
    {
        var configuration = Configuration(3);
        var manager = new TunerSessionManager(
            configuration,
            new TunerDeviceFactory(configuration),
            TimeProvider.System,
            NullLogger<TunerSessionManager>.Instance
        );

        var budget = DriverShutdownBudget.From(configuration);

        Assert.Equal(manager.ShutdownBudget + DriverShutdownBudget.DefaultHostSlack, budget.Total);
    }

    [Fact]
    public void ANegativeLingerCapDoesNotShortenTheBudget()
    {
        var budget = DriverShutdownBudget.From(Configuration(-1));

        Assert.Equal(TimeSpan.Zero, budget.Drain);
        Assert.Equal(30 + 60, budget.TotalSeconds);
    }

    [Fact]
    public void TheDescriptionNamesEveryPartOfTheSum()
    {
        var description = DriverShutdownBudget.From(Configuration(6)).Describe();

        Assert.Contains("21690s", description, StringComparison.Ordinal);
        Assert.Contains("21600s", description, StringComparison.Ordinal);
        Assert.Contains("30s", description, StringComparison.Ordinal);
        Assert.Contains("60s", description, StringComparison.Ordinal);
    }
}
