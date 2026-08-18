using System.Net;
using System.Net.Http.Json;

using Carina.Contracts;

namespace Carina.Driver.Tests;

public sealed class TunerToggleApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static HttpContent Body(bool? disabled) =>
        JsonContent.Create(
            new TunerToggleRequest { Disabled = disabled },
            DriverJson.Context.TunerToggleRequest
        );

    [Fact]
    public async Task TurningATunerOffTakesEffectAtOnceWhenNothingIsUsingIt()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        var tuner = await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.Equal(TunerState.Disabled, tuner.State);
        Assert.Equal(TunerState.Disabled, (await Tuner(client, "fake-terrestrial")).State);
    }

    [Fact]
    public async Task ATunerTurnedOffIsNoLongerHandedOut()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);

        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("s-1", "fake-terrestrial")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "disabledDevice",
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public async Task ATunerTurnedBackOnIsHandedOutAgainWithoutARestart()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-spare", disabled: false);

        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("s-1", "fake-spare")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ATunerInUseIsNotYankedOutFromUnderTheSessionThatHoldsIt()
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

        await Toggle(client, "fake-terrestrial", disabled: true);

        using var session = await client.GetAsync($"{DriverEndpoints.Sessions}/s-1", Soon());

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var snapshot = await DriverUnderTest.Read(session, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.Concluded);
    }

    [Fact]
    public async Task TurningOffATunerInUseIsAnsweredAsDrainingRatherThanAsDisabled()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Occupy(client, "fake-terrestrial");

        var tuner = await Toggle(client, "fake-terrestrial", disabled: true);

        Assert.Equal(TunerState.Draining, tuner.State);
        Assert.Equal(TunerState.Draining, (await Tuner(client, "fake-terrestrial")).State);
    }

    [Fact]
    public async Task ADrainingTunerIsRefusedToTheNextSessionThatAsksForIt()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Occupy(client, "fake-terrestrial");
        await Toggle(client, "fake-terrestrial", disabled: true);

        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("s-2", "fake-terrestrial")),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "disabledDevice",
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public async Task ADrainingTunerSettlesIntoBeingOffWhenTheSessionHoldingItEnds()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Occupy(client, "fake-terrestrial");
        await Toggle(client, "fake-terrestrial", disabled: true);

        using (
            var stopped = await client.DeleteAsync($"{DriverEndpoints.Sessions}/s-1?reason=test", Soon())
        )
        {
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        }

        await WaitUntil(
            client,
            tuners =>
                tuners.Any(tuner =>
                    tuner.DeviceId is "fake-terrestrial" && tuner.State is TunerState.Disabled
                )
        );
    }

    [Fact]
    public async Task ATunerTurnedBackOnWhileDrainingIsInServiceAgainRatherThanStillOnItsWayOut()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Occupy(client, "fake-terrestrial");
        await Toggle(client, "fake-terrestrial", disabled: true);

        var tuner = await Toggle(client, "fake-terrestrial", disabled: false);

        Assert.Equal(TunerState.Busy, tuner.State);
    }

    [Fact]
    public async Task ATunerNobodyHasHeardOfIsNotToggled()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PatchAsync(
            DriverEndpoints.Tuner("adapter9.frontend0"),
            Body(disabled: true),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "noSuchTuner",
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public async Task AToggleThatSaysNothingIsRefusedRatherThanReadAsTurningATunerOn()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PatchAsync(
            DriverEndpoints.Tuner("fake-terrestrial"),
            Body(disabled: null),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "rejected",
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.Equal(TunerState.Idle, (await Tuner(client, "fake-terrestrial")).State);
    }

    [Fact]
    public async Task ATunerIdOutsideTheShapeIsRefusedBeforeAnyTunerIsLookedUp()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PatchAsync(
            "/tuners/..%2Fsecrets",
            Body(disabled: true),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "badDeviceId",
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public async Task ADriverThatCanTurnATunerOffWhileItRunsSaysSoInItsGreeting()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        var hello = await DriverUnderTest.Read(
            await client.GetAsync(DriverEndpoints.Health, Soon()),
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.LiveTunerToggle));
    }

    [Fact]
    public async Task TurningATunerOffDoesNotChangeTheLedgerThatIsSaved()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Toggle(client, "fake-terrestrial", disabled: true);

        using var response = await client.GetAsync(DriverEndpoints.TunerLedger, Soon());
        var ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.False(ledger.HasDrifted());
        Assert.False(
            Assert.Single(ledger.Tuners, entry => entry.DeviceId is "fake-terrestrial").Disabled
        );
    }

    [Fact]
    public async Task NoDeviceNodeCrossesTheSocketWhenATunerIsToggled()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var answered = await client.PatchAsync(
            DriverEndpoints.Tuner("fake-terrestrial"),
            Body(disabled: true),
            Soon()
        );

        using var refused = await client.PatchAsync(
            DriverEndpoints.Tuner("adapter9.frontend0"),
            Body(disabled: true),
            Soon()
        );

        foreach (var response in new[] { answered, refused })
        {
            var body = await response.Content.ReadAsStringAsync(Soon());

            Assert.DoesNotContain("/dev", body, StringComparison.Ordinal);
            Assert.DoesNotContain("devicePath", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ADriverWhoseTunersAreAllDrainingIsNotHealthy()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        await Occupy(client, "fake-terrestrial");
        await Toggle(client, "fake-terrestrial", disabled: true);
        await Toggle(client, "fake-satellite", disabled: true);

        var tuners = await Tuners(client);

        Assert.False(
            Ipc.DriverProbe
                .Judge(new DriverHello(DriverProtocol.Version, "b7f2c9", []), tuners)
                .Healthy
        );
    }

    private static async Task Occupy(HttpClient client, string deviceId)
    {
        using var response = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(DriverUnderTest.Live("s-1", deviceId)),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

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
        Assert.Equal(deviceId, tuner.DeviceId);

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

    private static async Task WaitUntil(
        HttpClient client,
        Func<IReadOnlyList<TunerSnapshot>, bool> settled
    )
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (settled(await Tuners(client)))
            {
                return;
            }

            await Task.Delay(25, Soon());
        }

        Assert.Fail("The tuners never settled into the state the toggle asked for.");
    }
}
