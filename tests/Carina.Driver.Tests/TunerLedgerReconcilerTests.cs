using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class TunerLedgerReconcilerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task ATunerThatReceivesWhatTheLedgerClaimsServesAsBefore()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        Assert.DoesNotContain(await Tuners(client), tuner => tuner.State is TunerState.Faulted);
    }

    [Fact]
    public async Task ATunerWhoseDeliverySystemContradictsTheLedgerIsFaultedAtStartup()
    {
        await using DriverUnderTest driver = await Started(Contradicting("fake-terrestrial"));
        using HttpClient client = driver.Client();

        TunerSnapshot tuner = Assert.Single(
            await Tuners(client),
            candidate => candidate.DeviceId is "fake-terrestrial"
        );

        Assert.Equal(TunerState.Faulted, tuner.State);
        Assert.NotNull(tuner.Detail);
        Assert.Contains("terrestrial", tuner.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("satellite", tuner.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ATunerThatAgreesIsLeftServingWhileTheContradictingOneIsFaulted()
    {
        await using DriverUnderTest driver = await Started(Contradicting("fake-terrestrial"));
        using HttpClient client = driver.Client();

        TunerSnapshot tuner = Assert.Single(
            await Tuners(client),
            candidate => candidate.DeviceId is "fake-satellite"
        );

        Assert.Equal(TunerState.Idle, tuner.State);
    }

    [Fact]
    public async Task TheContradictionIsRecordedWhereItCanBeReadBackAfterwards()
    {
        await using DriverUnderTest driver = await Started(Contradicting("fake-terrestrial"));
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Diagnostics, Soon());

        IReadOnlyList<DiagnosticSnapshot>? diagnostics = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListDiagnosticSnapshot
        );

        Assert.NotNull(diagnostics);

        DiagnosticSnapshot recorded = Assert.Single(
            diagnostics,
            entry => entry.DeviceId is "fake-terrestrial"
        );

        Assert.Equal(DiagnosticReason.DeviceFaulted, recorded.Reason);
        Assert.NotNull(recorded.Detail);
    }

    [Fact]
    public async Task ASessionIsRefusedOnATunerThatContradictsTheLedgerRatherThanTunedBlind()
    {
        await using DriverUnderTest driver = await Started(Contradicting("fake-terrestrial"));
        using HttpClient client = driver.Client();

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("blind-1", "fake-terrestrial")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem);

        Assert.NotNull(problem);
        Assert.Equal("faultedDevice", problem.Title);
    }

    [Fact]
    public async Task ATunerAnotherProcessHeldAtStartupIsNotFaultedForSayingNothing()
    {
        var detector = new ScriptedTunerDetector(
            new TunerDetection(
                "fake-terrestrial",
                [],
                DeviceDetection.Busy,
                "another process is already holding this tuner"
            )
        );

        await using DriverUnderTest driver = await Started(detector);
        using HttpClient client = driver.Client();

        Assert.DoesNotContain(await Tuners(client), tuner => tuner.State is TunerState.Faulted);
    }

    [Fact]
    public async Task TheDriverIsUnhealthyWhenEveryUsableTunerContradictsTheLedger()
    {
        await using DriverUnderTest driver = await Started(
            Contradicting("fake-terrestrial", "fake-satellite")
        );

        ProbeVerdict verdict = await DriverProbe.AskAsync(driver.Configuration, Patience);

        Assert.False(verdict.Healthy);
        Assert.Contains("faulted", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLedgerIsCheckedOnceAtStartupRatherThanOnEveryRequest()
    {
        ScriptedTunerDetector detector = Contradicting("fake-terrestrial");

        await using DriverUnderTest driver = await Started(detector);
        using HttpClient client = driver.Client();

        await Tuners(client);
        await Tuners(client);

        Assert.Equal(1, detector.Detections);
    }

    private static ScriptedTunerDetector Contradicting(params string[] deviceIds) =>
        new(
            [
                .. deviceIds.Select(deviceId => new TunerDetection(
                    deviceId,
                    [Opposite(deviceId)],
                    DeviceDetection.Detected,
                    null
                )),
            ]
        );

    private static DeviceKind Opposite(string deviceId) =>
        deviceId.Contains("satellite", StringComparison.Ordinal)
            ? DeviceKind.Terrestrial
            : DeviceKind.Satellite;

    private static Task<DriverUnderTest> Started(ITunerDetector detector) =>
        DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton(detector)
        );

    private static async Task<IReadOnlyList<TunerSnapshot>> Tuners(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Tuners, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<TunerSnapshot>? tuners = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);

        return tuners;
    }
}
