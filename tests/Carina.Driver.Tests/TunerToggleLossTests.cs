using System.Net;
using System.Net.Http.Json;

using Carina.Contracts;

namespace Carina.Driver.Tests;

public sealed class TunerToggleLossTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static HttpContent Body(bool disabled) =>
        JsonContent.Create(
            new TunerToggleRequest { Disabled = disabled },
            DriverJson.Context.TunerToggleRequest
        );

    [Fact]
    public async Task ATunerTurnedOffAtRuntimeSaysThatIsWhyItIsOff()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.True((await Tuner(client, "fake-terrestrial")).Toggled);
    }

    [Fact]
    public async Task ATunerTheLedgerItselfTurnsOffIsNotMarkedAsToggled()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        var tuner = await Tuner(client, "fake-spare");

        Assert.Equal(TunerState.Disabled, tuner.State);
        Assert.False(tuner.Toggled);
    }

    [Fact]
    public async Task ATunerTurnedOnAtRuntimeAgainstTheLedgerIsMarkedJustTheSame()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-spare", disabled: false);

        var tuner = await Tuner(client, "fake-spare");

        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.True(tuner.Toggled);
    }

    [Fact]
    public async Task ADrainingTunerIsMarkedToggledWhileItFinishesWhatItHolds()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using (
            var started = await client.PostAsync(
                DriverEndpoints.Sessions,
                DriverUnderTest.Body(DriverUnderTest.Live("s-1", "fake-terrestrial")),
                Soon()
            )
        )
        {
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        }

        var tuner = await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.Equal(TunerState.Draining, tuner.State);
        Assert.True(tuner.Toggled);
    }

    [Fact]
    public async Task PuttingATunerBackTheWayTheLedgerHasItLeavesNothingToLose()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);
        await Toggle(client, "fake-terrestrial", disabled: false);

        Assert.False((await Tuner(client, "fake-terrestrial")).Toggled);
    }

    [Fact]
    public async Task ARestartPutsTheTunerBackAsTheLedgerHasItAndKeepsNoTraceOfTheToggle()
    {
        await using var driver = await DriverUnderTest.Start();

        using (var client = driver.Client())
        {
            await Toggle(client, "fake-terrestrial", disabled: true);
        }

        await driver.RestartOnTheSameLedger();

        using var restarted = driver.Client();
        var tuner = await Tuner(restarted, "fake-terrestrial");

        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.False(tuner.Toggled);
    }

    [Fact]
    public async Task ARestartAfterAToggleShowsNoDriftBecauseAToggleNeverReachedTheLedger()
    {
        await using var driver = await DriverUnderTest.Start();

        using (var client = driver.Client())
        {
            await Toggle(client, "fake-terrestrial", disabled: true);
        }

        await driver.RestartOnTheSameLedger();

        using var restarted = driver.Client();

        using var response = await restarted.GetAsync(DriverEndpoints.TunerLedger, Soon());
        var ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.False(ledger.HasDrifted());
    }

    [Fact]
    public async Task WhileNothingHasDriftedTheLedgerAndTheTunersTogetherNameEveryToggle()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);
        await Toggle(client, "fake-spare", disabled: false);

        using var response = await client.GetAsync(DriverEndpoints.TunerLedger, Soon());
        var ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.False(ledger.HasDrifted());

        var tuners = await Tuners(client);

        var derived = tuners
            .Where(tuner =>
                ledger.Tuners.Single(entry => entry.DeviceId == tuner.DeviceId).Disabled
                != OutOfService(tuner)
            )
            .Select(tuner => tuner.DeviceId);

        Assert.Equal(
            derived,
            tuners.Where(tuner => tuner.Toggled).Select(tuner => tuner.DeviceId)
        );
        Assert.Equal(["fake-terrestrial", "fake-spare"], derived);
    }

    private static bool OutOfService(TunerSnapshot tuner) =>
        tuner.State is TunerState.Disabled or TunerState.Draining;

    private static async Task<TunerSnapshot> Toggle(
        HttpClient client,
        string deviceId,
        bool disabled
    )
    {
        using var response = await client.PatchAsync(
            DriverEndpoints.Tuner(deviceId),
            Body(disabled),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tuner = await DriverUnderTest.Read(response, DriverJson.Context.TunerSnapshot);

        Assert.NotNull(tuner);

        return tuner;
    }

    private static async Task<IReadOnlyList<TunerSnapshot>> Tuners(HttpClient client)
    {
        using var response = await client.GetAsync(DriverEndpoints.Tuners, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tuners = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);

        return tuners;
    }

    private static async Task<TunerSnapshot> Tuner(HttpClient client, string deviceId) =>
        Assert.Single(await Tuners(client), tuner => tuner.DeviceId == deviceId);
}
