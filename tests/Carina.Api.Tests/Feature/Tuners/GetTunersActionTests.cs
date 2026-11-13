using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.TunerLedgerFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[Trait("Category", "Feature")]
public sealed class GetTunersActionTests
{
    private static readonly DateTimeOffset Ends = Started.AddMinutes(30);

    [Fact]
    public async Task AnObservedTunerCarriesItsHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), HardwareStock.Stocked);

        JsonElement tuner = await ObservedAsync(feature, "adapter0");
        JsonElement hardware = tuner.GetProperty("hardware");

        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("machine").ValueKind);
        Assert.Equal("Example ISDB-T frontend", hardware.GetProperty("name").GetString());
        Assert.Equal("example_dvb", hardware.GetProperty("kernelDriver").GetString());
        Assert.Equal("pci", hardware.GetProperty("bus").GetString());
        Assert.Equal("/dev/dvb/adapter0/frontend0", hardware.GetProperty("devicePath").GetString());
        Assert.Equal(JsonValueKind.Null, hardware.GetProperty("usbPort").ValueKind);
        Assert.True(hardware.GetProperty("present").GetBoolean());
        Assert.Equal(HardwareStock.Moment, hardware.GetProperty("lastSeenAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ATunerFromAnOldDriverHasNoHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), HardwareStock.Stocked);

        Assert.Equal(JsonValueKind.Null, (await ObservedAsync(feature, "adapter1")).GetProperty("hardware").ValueKind);
    }

    [Fact]
    public async Task ATunerRecordingIsNamedForTheProgrammeItRecords()
    {
        SessionId session = SessionId.Parse("rec-0123456789abcdef0123456789abcdef");
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), driver =>
        {
            HardwareStock.Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Busy, session)
                {
                    CurrentSession = new CurrentSessionDto { SessionId = session, Purpose = SessionPurpose.Recording },
                },
            ];
        });
        feature.Titles.Titles[session] = "Example Programme";

        JsonElement tuner = await ObservedAsync(feature, "adapter0");

        Assert.Equal("Example Programme", tuner.GetProperty("sessionTitle").GetString());
    }

    [Fact]
    public async Task TheTunersAreStillAnsweredWhenTheRecordingsCannotBeRead()
    {
        SessionId session = SessionId.Parse("rec-0123456789abcdef0123456789abcdef");
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), driver =>
        {
            HardwareStock.Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Busy, session)
                {
                    CurrentSession = new CurrentSessionDto { SessionId = session, Purpose = SessionPurpose.Recording },
                },
            ];
        });
        feature.Titles.Failure = new InvalidOperationException("synthetic: the database went away");

        JsonElement tuner = await ObservedAsync(feature, "adapter0");

        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("sessionTitle").ValueKind);
        Assert.Equal("recording", tuner.GetProperty("sessionPurpose").GetString());
    }

    [Fact]
    public async Task NoRecordingIsLookedUpWhileNoTunerRecords()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), HardwareStock.Stocked);

        JsonElement tuner = await ObservedAsync(feature, "adapter0");

        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("sessionTitle").ValueKind);
        Assert.Equal(0, feature.Titles.Asked);
    }

    [Fact]
    public async Task TheLedgerKeepsWhatWasSavedApartFromWhatIsRunning()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

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

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

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

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
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

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
        JsonElement data = body.GetProperty("data");

        Assert.True(data.GetProperty("drifted").GetBoolean());
        Assert.Equal("saved", data.GetProperty("savedHash").GetString());
        Assert.Equal("loaded", data.GetProperty("loadedHash").GetString());
    }

    [Fact]
    public async Task ALedgerThatMatchesWhatTheDriverLoadedDoesNotClaimDrift()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

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

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
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

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact]
    public async Task ADriverTooOldToHoldALedgerIsAnsweredAsUnimplementedRatherThanBroken()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.Recording]),
            Stocked);

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Contains(
            DriverCapabilities.TunerLedger,
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFaultedTunerNamesTheKindOfFaultRatherThanOnlyTheDriversSentence()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Faulted)
                {
                    Health = new TunerHealthDto
                    {
                        Level = TunerHealthLevel.Faulted,
                        Detail = "the ledger and the tuner disagree",
                        FaultKind = TunerFaultKind.LedgerDisagrees,
                        FaultDeclaredKind = TunerKind.Terrestrial,
                        FaultReceivableKinds = [TunerKind.Satellite],
                    },
                },
            ];
        });

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
        JsonElement observed = body.GetProperty("data").GetProperty("observed")[0];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ledgerDisagrees", observed.GetProperty("faultKind").GetString());
        Assert.Equal("terrestrial", observed.GetProperty("faultDeclaredKind").GetString());
        Assert.Equal(
            ["satellite"],
            observed.GetProperty("faultReceivableKinds").EnumerateArray().Select(kind => kind.GetString()));
    }

    [Fact]
    public async Task AFaultedTunerFromADriverThatNamesNoKindSaysUnspecified()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Faulted)
                {
                    Health = new TunerHealthDto { Level = TunerHealthLevel.Faulted, Detail = "something broke" },
                },
            ];
        });

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
        JsonElement observed = body.GetProperty("data").GetProperty("observed")[0];

        Assert.Equal("unspecified", observed.GetProperty("faultKind").GetString());
        Assert.Equal(JsonValueKind.Null, observed.GetProperty("faultDeclaredKind").ValueKind);
        Assert.Empty(observed.GetProperty("faultReceivableKinds").EnumerateArray());
        Assert.Equal("unspecified", observed.GetProperty("degradedKind").GetString());
    }

    [Fact]
    public async Task ADegradedTunerNamesTheKindOfDegradation()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)
                {
                    Health = new TunerHealthDto
                    {
                        Level = TunerHealthLevel.Degraded,
                        DegradedKind = TunerDegradedKind.TuneFailing,
                    },
                },
            ];
        });

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));
        JsonElement observed = body.GetProperty("data").GetProperty("observed")[0];

        Assert.Equal("degraded", observed.GetProperty("health").GetString());
        Assert.Equal("tuneFailing", observed.GetProperty("degradedKind").GetString());
    }

    [Fact]
    public async Task AMachineWithNothingConfiguredIsSetUpThroughTheApiAndNowhereElse()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        (HttpStatusCode empty, JsonElement nothing) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

        Assert.Equal(HttpStatusCode.OK, empty);
        Assert.Equal(0, nothing.GetProperty("data").GetProperty("desired").GetArrayLength());
    }

    [Fact]
    public async Task EveryLedgerSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TunersPath)).StatusCode);
    }

    private static async Task<JsonElement> ObservedAsync(DriverFeature feature, string deviceId)
    {
        using HttpResponseMessage response = await feature.Client.GetAsync(TunersPath);
        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("observed").EnumerateArray()
            .Single(tuner => tuner.GetProperty("deviceId").GetString() == deviceId);
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

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

        return body.GetProperty("data").GetProperty("observed")[0].Clone();
    }
}
