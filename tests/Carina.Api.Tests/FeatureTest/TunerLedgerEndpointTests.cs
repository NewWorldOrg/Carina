using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Contracts;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class TunerLedgerEndpointTests
{
    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);
    private static readonly Uri Detected = new("/api/tuners/detected", UriKind.Relative);

    private static readonly string[] Everything =
    [
        DriverCapabilities.TunerLedger,
        DriverCapabilities.DeviceDetection,
        DriverCapabilities.LiveTunerToggle,
    ];

    private static DriverHello Capable(string[]? capabilities = null)
        => FakeDriver.HelloFor("instance-a", capabilities: capabilities ?? Everything);

    private static void Stocked(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto
        {
            Tuners =
            [
                new TunerConfigEntry { DeviceId = "adapter0" },
                new TunerConfigEntry { DeviceId = "adapter1", Disabled = true },
            ],
            SavedHash = "saved",
            LoadedHash = "saved",
        };

        driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
            new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
        ];

        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
            },
            new DetectedDeviceDto
            {
                DeviceId = "adapter1",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Satellite],
            },
        ];
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task TheLedgerKeepsWhatWasSavedApartFromWhatIsRunning()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        var (status, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.OK, status);

        var data = body.GetProperty("data");

        Assert.Equal(
            ["adapter0", "adapter1"],
            data.GetProperty("desired").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
        Assert.Equal(
            ["adapter0", "adapter1"],
            data.GetProperty("observed").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
        Assert.True(data.GetProperty("desired")[1].GetProperty("disabled").GetBoolean());
        Assert.Equal("idle", data.GetProperty("observed")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task TheObservationCarriesTheMomentItWasTakenSoTheScreenCanSayHowOldItIs()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        var (_, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.NotEqual(
            default,
            body.GetProperty("data").GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task DriftBetweenTheSavedAndTheLoadedLedgerIsVisibleToTheClient()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Ledger = driver.Ledger with { SavedHash = "saved", LoadedHash = "loaded" };
        });

        var (_, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));
        var data = body.GetProperty("data");

        Assert.True(data.GetProperty("drifted").GetBoolean());
        Assert.Equal("saved", data.GetProperty("savedHash").GetString());
        Assert.Equal("loaded", data.GetProperty("loadedHash").GetString());
    }

    [Fact]
    public async Task ALedgerThatMatchesWhatTheDriverLoadedDoesNotClaimDrift()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        var (_, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.False(body.GetProperty("data").GetProperty("drifted").GetBoolean());
    }

    [Fact]
    public async Task TheSavedLedgerStillReadsWhenTheRunningTunersCannotBeAsked()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.RefusalsByPath[DriverEndpoints.Tuners] = new FakeDriver.Refusal(
                StatusCodes.Status503ServiceUnavailable,
                new DriverProblem("draining", ["The driver is shutting down."]));
        });

        var (status, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));
        var data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data.GetProperty("desired").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("observed").ValueKind);
        Assert.Contains("draining", data.GetProperty("observationFailure").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatIsNotThereIsAnsweredApartFromADriverThatSaidNo()
    {
        await using var feature = await DriverFeature.StartAsync();

        var (status, _) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact]
    public async Task ADriverTooOldToHoldALedgerIsAnsweredAsUnimplementedRatherThanBroken()
    {
        await using var feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.Recording]),
            Stocked);

        var (status, body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains(
            DriverCapabilities.TunerLedger,
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectionNamesWhatIsNewWhatIsGoneAndWhatChangedItsKind()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.DetectedDevices =
            [
                new DetectedDeviceDto
                {
                    DeviceId = "adapter0",
                    Detection = DeviceDetection.Detected,
                    Kinds = [TunerKind.Satellite],
                },
                new DetectedDeviceDto
                {
                    DeviceId = "adapter2",
                    Detection = DeviceDetection.Detected,
                    Kinds = [TunerKind.Terrestrial],
                },
            ];
        });

        var (status, body) = await ReadAsync(await feature.Client.GetAsync(Detected));
        var data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter2"],
            data.GetProperty("added").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(
            ["adapter1"],
            data.GetProperty("missing").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(
            ["adapter0"],
            data.GetProperty("mismatched").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
    }

    [Fact]
    public async Task AnUndisturbedLedgerShowsNoDetectionDifference()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        var (_, body) = await ReadAsync(await feature.Client.GetAsync(Detected));
        var data = body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("added").GetArrayLength());
        Assert.Equal(0, data.GetProperty("missing").GetArrayLength());
        Assert.Equal(0, data.GetProperty("mismatched").GetArrayLength());
    }

    [Fact]
    public async Task SavingALedgerHandsTheDriverOnlyTheDeviceIdsItDetected()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = true } },
        });

        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter0"],
            feature.Driver.LastReplacedLedger!.Select(entry => entry.DeviceId));
        Assert.True(feature.Driver.LastReplacedLedger![0].LnbPower);
    }

    [Fact]
    public async Task AnEmptyLedgerIsRefusedBeforeTheDriverIsEvenAsked()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PutAsJsonAsync(Tuners, new { tuners = Array.Empty<object>() });
        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Fact]
    public async Task ALedgerNamingSomethingThatIsNotADeviceIdIsRefused()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[] { new { deviceId = "../../dev/dvb/adapter0/frontend0" } },
        });

        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Fact]
    public async Task TogglingATunerReachesTheDriverAndReportsWhatItAnswered()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("adapter0", feature.Driver.LastToggledDeviceId);
        Assert.True(feature.Driver.LastToggle!.Disabled);
        Assert.Equal("adapter0", body.GetProperty("data").GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ATunerBusyWhenDisabledIsReportedAsPendingRatherThanDone()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Draining)
                {
                    Health = new TunerHealthDto { Level = TunerHealthLevel.Healthy, DisablePending = true },
                },
            ];
        });

        using var response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        var (_, body) = await ReadAsync(response);
        var data = body.GetProperty("data");

        Assert.Equal("draining", data.GetProperty("state").GetString());
        Assert.True(data.GetProperty("disablePending").GetBoolean());
    }

    [Fact]
    public async Task TogglingATunerTheDriverDoesNotHoldIsAnsweredAsNotFound()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter9", UriKind.Relative),
            new { disabled = true });

        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ADriverThatCannotToggleWhileRunningSaysSoWithoutFailingTheScreen()
    {
        await using var feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.TunerLedger]),
            Stocked);

        using var response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, status);
    }

    [Fact]
    public async Task AToggleWithoutASideIsRefusedBeforeTheDriverIsAsked()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { });

        var (status, _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastToggledDeviceId);
    }

    [Fact]
    public async Task EveryLedgerSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Stocked);
        using var app = new TestingWebApplicationFactory();
        using var client = app.WithTestScheme().CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Tuners)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Detected)).StatusCode);
    }
}
