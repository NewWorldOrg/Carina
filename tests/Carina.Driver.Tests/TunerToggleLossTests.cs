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
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.True((await Tuner(client, "fake-terrestrial")).Toggled);
    }

    [Fact]
    public async Task ATunerTheLedgerItselfTurnsOffIsNotMarkedAsToggled()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        TunerSnapshot tuner = await Tuner(client, "fake-spare");

        Assert.Equal(TunerState.Disabled, tuner.State);
        Assert.False(tuner.Toggled);
    }

    [Fact]
    public async Task ATunerTurnedOnAtRuntimeAgainstTheLedgerIsMarkedJustTheSame()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        await Toggle(client, "fake-spare", disabled: false);

        TunerSnapshot tuner = await Tuner(client, "fake-spare");

        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.True(tuner.Toggled);
    }

    [Fact]
    public async Task ADrainingTunerIsMarkedToggledWhileItFinishesWhatItHolds()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using (
            HttpResponseMessage started = await client.PostAsync(
                DriverEndpoints.Sessions,
                DriverUnderTest.Body(DriverUnderTest.Live("s-1", "fake-terrestrial")),
                Soon()
            )
        )
        {
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        }

        TunerSnapshot tuner = await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.Equal(TunerState.Draining, tuner.State);
        Assert.True(tuner.Toggled);
    }

    [Fact]
    public async Task PuttingATunerBackTheWayTheLedgerHasItLeavesNothingToLose()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);
        await Toggle(client, "fake-terrestrial", disabled: false);

        Assert.False((await Tuner(client, "fake-terrestrial")).Toggled);
    }

    [Fact]
    public async Task ARestartPutsTheTunerBackAsTheLedgerHasItAndKeepsNoTraceOfTheToggle()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        using (HttpClient client = driver.Client())
        {
            await Toggle(client, "fake-terrestrial", disabled: true);
        }

        await driver.RestartOnTheSameLedger();

        using HttpClient restarted = driver.Client();
        TunerSnapshot tuner = await Tuner(restarted, "fake-terrestrial");

        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.False(tuner.Toggled);
    }

    [Fact]
    public async Task ARestartAfterAToggleShowsNoDriftBecauseAToggleNeverReachedTheLedger()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();

        using (HttpClient client = driver.Client())
        {
            await Toggle(client, "fake-terrestrial", disabled: true);
        }

        await driver.RestartOnTheSameLedger();

        using HttpClient restarted = driver.Client();

        using HttpResponseMessage response = await restarted.GetAsync(DriverEndpoints.TunerLedger, Soon());
        TunerLedgerDto? ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.False(ledger.HasDrifted());
    }

    [Fact]
    public async Task WhileNothingHasDriftedTheLedgerAndTheTunersTogetherNameEveryToggle()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);
        await Toggle(client, "fake-spare", disabled: false);

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.TunerLedger, Soon());
        TunerLedgerDto? ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.False(ledger.HasDrifted());

        IReadOnlyList<TunerSnapshot> tuners = await Tuners(client);

        IEnumerable<string> derived = tuners
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
        using HttpResponseMessage response = await client.PatchAsync(
            DriverEndpoints.Tuner(deviceId),
            Body(disabled),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TunerSnapshot? tuner = await DriverUnderTest.Read(response, DriverJson.Context.TunerSnapshot);

        Assert.NotNull(tuner);

        return tuner;
    }

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

    private static async Task<TunerSnapshot> Tuner(HttpClient client, string deviceId) =>
        Assert.Single(await Tuners(client), tuner => tuner.DeviceId == deviceId);
}
