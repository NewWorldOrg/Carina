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

    private static readonly DateTimeOffset Started =
        new(2026, 8, 12, 21, 0, 0, TimeSpan.FromHours(9));

    private static readonly DateTimeOffset Ends = Started.AddMinutes(30);

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

    private static void Holding(FakeDriver driver, SessionPurpose purpose, TuneParams tune, DateTimeOffset? endsAt)
    {
        Stocked(driver);

        driver.Tuners =
        [
            new TunerSnapshot(
                "adapter0",
                tune.Kind,
                TunerState.Busy,
                SessionId.Parse("holding-one"))
            {
                CurrentSession = new CurrentSessionDto
                {
                    SessionId = SessionId.Parse("holding-one"),
                    Purpose = purpose,
                    StartedAt = Started,
                    EndsAt = endsAt,
                    Tune = tune,
                },
            },
        ];
    }

    private static async Task<JsonElement> HeldSessionAsync(
        SessionPurpose purpose,
        TuneParams tune,
        DateTimeOffset? endsAt = null)
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable(),
            driver => Holding(driver, purpose, tune, endsAt));

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        return body.GetProperty("data").GetProperty("observed")[0].Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }

    private static void Unconfigured(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto { SavedHash = "empty", LoadedHash = "empty" };

        driver.Tuners = [];

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

    [Fact]
    public async Task AMachineWithNothingConfiguredIsSetUpThroughTheApiAndNowhereElse()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        (HttpStatusCode empty, JsonElement nothing) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.OK, empty);
        Assert.Equal(0, nothing.GetProperty("data").GetProperty("desired").GetArrayLength());

        (HttpStatusCode looked, JsonElement found) = await ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement difference = found.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, looked);
        Assert.Equal(
            ["adapter0", "adapter1"],
            difference.GetProperty("added").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(0, difference.GetProperty("missing").GetArrayLength());

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[]
            {
                new { deviceId = "adapter0", disabled = false, lnbPower = false },
                new { deviceId = "adapter1", disabled = false, lnbPower = false },
            },
        });

        (HttpStatusCode saved, JsonElement _) = await ReadAsync(saving);

        Assert.Equal(HttpStatusCode.OK, saved);

        (HttpStatusCode listed, JsonElement ledger) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.Equal(
            ["adapter0", "adapter1"],
            ledger.GetProperty("data").GetProperty("desired").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
    }

    [Fact]
    public async Task AFreshlySavedLedgerSaysItIsAheadOfWhatTheDriverIsRunning()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = false } },
        });

        (HttpStatusCode _, JsonElement body) = await ReadAsync(saving);

        Assert.True(body.GetProperty("data").GetProperty("drifted").GetBoolean());
    }

    [Fact]
    public async Task TheLedgerKeepsWhatWasSavedApartFromWhatIsRunning()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");

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
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.NotEqual(
            default,
            body.GetProperty("data").GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task TheChannelTheDriverSaysATunerIsOnReachesTheScreenAndNotOnlyThePurpose()
    {
        JsonElement observed = await HeldSessionAsync(SessionPurpose.Recording, TuneParams.Terrestrial(57));
        JsonElement tuning = observed.GetProperty("sessionTuning");

        Assert.Equal("recording", observed.GetProperty("sessionPurpose").GetString());
        Assert.Equal("isdbT", tuning.GetProperty("system").GetString());
        Assert.Equal(57, tuning.GetProperty("physicalChannel").GetInt32());
    }

    [Fact]
    public async Task TheEndOfTheRecordingHoldingATunerReachesTheScreenThatHasToNameIt()
    {
        JsonElement observed = await HeldSessionAsync(
            SessionPurpose.Recording,
            TuneParams.Terrestrial(53),
            Ends);

        Assert.Equal(Ends, observed.GetProperty("sessionEndsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task TheMomentASessionStartedSurvivesTheTripFromTheDriverToTheScreen()
    {
        JsonElement observed = await HeldSessionAsync(SessionPurpose.Live, TuneParams.Terrestrial(55));

        Assert.Equal(Started, observed.GetProperty("sessionStartedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ASessionOnASatelliteSlotCarriesTheTransportStreamThatTellsItApart()
    {
        JsonElement observed = await HeldSessionAsync(SessionPurpose.Live, TuneParams.Bs(1, 50001));
        JsonElement tuning = observed.GetProperty("sessionTuning");

        Assert.Equal("isdbSBs", tuning.GetProperty("system").GetString());
        Assert.Equal(1, tuning.GetProperty("physicalChannel").GetInt32());
        Assert.Equal(50001, tuning.GetProperty("transportStreamId").GetInt32());
    }

    [Fact]
    public async Task ADriverTooOldToNameWhenASessionEndsLeavesTheFieldEmptyRatherThanInventingAMoment()
    {
        JsonElement observed = await HeldSessionAsync(SessionPurpose.Live, TuneParams.Terrestrial(55));

        Assert.Equal(JsonValueKind.Null, observed.GetProperty("sessionEndsAt").ValueKind);
    }

    [Fact]
    public async Task ATunerHoldingNothingCarriesNoTuningRatherThanAChannelOfZero()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));
        JsonElement observed = body.GetProperty("data").GetProperty("observed")[0];

        Assert.Equal(JsonValueKind.Null, observed.GetProperty("sessionTuning").ValueKind);
        Assert.Equal(JsonValueKind.Null, observed.GetProperty("sessionStartedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, observed.GetProperty("sessionEndsAt").ValueKind);
    }

    [Fact]
    public async Task DriftBetweenTheSavedAndTheLoadedLedgerIsVisibleToTheClient()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Ledger = driver.Ledger with { SavedHash = "saved", LoadedHash = "loaded" };
        });

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));
        JsonElement data = body.GetProperty("data");

        Assert.True(data.GetProperty("drifted").GetBoolean());
        Assert.Equal("saved", data.GetProperty("savedHash").GetString());
        Assert.Equal("loaded", data.GetProperty("loadedHash").GetString());
    }

    [Fact]
    public async Task ALedgerThatMatchesWhatTheDriverLoadedDoesNotClaimDrift()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.False(body.GetProperty("data").GetProperty("drifted").GetBoolean());
    }

    [Fact]
    public async Task TheSavedLedgerStillReadsWhenTheRunningTunersCannotBeAsked()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.RefusalsByPath[DriverEndpoints.Tuners] = new FakeDriver.Refusal(
                StatusCodes.Status503ServiceUnavailable,
                new DriverProblem("draining", ["The driver is shutting down."]));
        });

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data.GetProperty("desired").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("observed").ValueKind);
        Assert.Contains("draining", data.GetProperty("observationFailure").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatIsNotThereIsAnsweredApartFromADriverThatSaidNo()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        (HttpStatusCode status, JsonElement _) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact]
    public async Task ADriverTooOldToHoldALedgerIsAnsweredAsUnimplementedRatherThanBroken()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.Recording]),
            Stocked);

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Tuners));

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains(
            DriverCapabilities.TunerLedger,
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectionNamesWhatIsNewWhatIsGoneAndWhatChangedItsKind()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
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

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement data = body.GetProperty("data");

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
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("added").GetArrayLength());
        Assert.Equal(0, data.GetProperty("missing").GetArrayLength());
        Assert.Equal(0, data.GetProperty("mismatched").GetArrayLength());
    }

    [Fact]
    public async Task SavingALedgerHandsTheDriverOnlyTheDeviceIdsItDetected()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = true } },
        });

        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter0"],
            feature.Driver.LastReplacedLedger!.Select(entry => entry.DeviceId));
        Assert.True(feature.Driver.LastReplacedLedger![0].LnbPower);
    }

    [Fact]
    public async Task AnEmptyLedgerIsRefusedBeforeTheDriverIsEvenAsked()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(Tuners, new { tuners = Array.Empty<object>() });
        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Fact]
    public async Task ALedgerNamingSomethingThatIsNotADeviceIdIsRefused()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[] { new { deviceId = "../../dev/dvb/adapter0/frontend0" } },
        });

        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Fact]
    public async Task TogglingATunerReachesTheDriverAndReportsWhatItAnswered()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("adapter0", feature.Driver.LastToggledDeviceId);
        Assert.True(feature.Driver.LastToggle!.Disabled);
        Assert.Equal("adapter0", body.GetProperty("data").GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ATunerBusyWhenDisabledIsReportedAsPendingRatherThanDone()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
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

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode _, JsonElement body) = await ReadAsync(response);
        JsonElement data = body.GetProperty("data");

        Assert.Equal("draining", data.GetProperty("state").GetString());
        Assert.True(data.GetProperty("disablePending").GetBoolean());
    }

    [Fact]
    public async Task TogglingATunerTheDriverDoesNotHoldIsAnsweredAsNotFound()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter9", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ADriverThatCannotToggleWhileRunningSaysSoWithoutFailingTheScreen()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.TunerLedger]),
            Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, status);
    }

    [Fact]
    public async Task AToggleWithoutASideIsRefusedBeforeTheDriverIsAsked()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { });

        (HttpStatusCode status, JsonElement _) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastToggledDeviceId);
    }

    [Fact]
    public async Task EveryLedgerSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Tuners)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Detected)).StatusCode);
    }
}
