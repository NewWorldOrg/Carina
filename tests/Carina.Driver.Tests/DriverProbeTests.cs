using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class DriverProbeTests
{
    private static readonly DriverHello Serving = new(
        DriverProtocol.Version,
        "instance",
        [DriverCapabilities.Recording]
    );

    private static TunerSnapshot Tuner(string id, TunerState state) =>
        new(id, TunerKind.Terrestrial, state);

    [Fact]
    public void ADriverThatDoesNotAnswerIsNotHealthy()
    {
        ProbeVerdict verdict = DriverProbe.Judge(null, [Tuner("adapter0", TunerState.Idle)]);

        Assert.False(verdict.Healthy);
        Assert.Contains("did not answer", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADrainingDriverIsNotHealthy()
    {
        ProbeVerdict verdict = DriverProbe.Judge(
            Serving with
            {
                Draining = true,
            },
            [Tuner("adapter0", TunerState.Idle)]
        );

        Assert.False(verdict.Healthy);
        Assert.Contains("draining", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTunerFaultedIsNotHealthy()
    {
        ProbeVerdict verdict = DriverProbe.Judge(
            Serving,
            [Tuner("adapter0", TunerState.Faulted), Tuner("adapter1", TunerState.Faulted)]
        );

        Assert.False(verdict.Healthy);
        Assert.Contains("adapter0", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("adapter1", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisabledTunerIsNotAFault()
    {
        ProbeVerdict verdict = DriverProbe.Judge(
            Serving,
            [Tuner("adapter0", TunerState.Idle), Tuner("adapter1", TunerState.Disabled)]
        );

        Assert.True(verdict.Healthy);
        Assert.Contains("1 tuners", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTunerDisabledIsNotHealthy()
    {
        ProbeVerdict verdict = DriverProbe.Judge(Serving, [Tuner("adapter0", TunerState.Disabled)]);

        Assert.False(verdict.Healthy);
        Assert.Contains("disabled", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OneFaultedTunerAmongUsableOnesStaysHealthyAndSaysSo()
    {
        ProbeVerdict verdict = DriverProbe.Judge(
            Serving,
            [
                Tuner("adapter0", TunerState.Busy),
                Tuner("adapter1", TunerState.Faulted),
                Tuner("adapter2", TunerState.Idle),
            ]
        );

        Assert.True(verdict.Healthy);
        Assert.Contains("adapter1", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("2 of 3", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriverWithoutTunersIsNotHealthy()
    {
        ProbeVerdict verdict = DriverProbe.Judge(Serving, []);

        Assert.False(verdict.Healthy);
        Assert.Contains("no tuner", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProbeReachesARunningDriverOverItsSocket()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        var output = new StringWriter();
        int exitCode = await DriverProbe.RunAsync(driver.Configuration, output);

        Assert.Equal(DriverProbe.HealthyExitCode, exitCode);
        Assert.Contains("serving", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProbeFailsWhenNothingListensOnTheSocket()
    {
        string root = DriverUnderTest.NewRoot();
        DriverConfiguration configuration = DriverUnderTest.ConfigurationIn(root);

        var output = new StringWriter();
        int exitCode = await DriverProbe.RunAsync(
            configuration,
            output,
            TimeSpan.FromSeconds(2)
        );

        Assert.Equal(DriverProbe.UnhealthyExitCode, exitCode);
        Assert.Contains("did not answer", output.ToString(), StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task TheProbeTurnsUnhealthyWhileTheDriverIsDraining()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        driver.Service<TunerSessionManager>().EnterDraining();

        var output = new StringWriter();
        int exitCode = await DriverProbe.RunAsync(driver.Configuration, output);

        Assert.Equal(DriverProbe.UnhealthyExitCode, exitCode);
        Assert.Contains("draining", output.ToString(), StringComparison.Ordinal);
    }
}
