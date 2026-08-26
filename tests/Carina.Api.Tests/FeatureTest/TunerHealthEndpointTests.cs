using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class TunerHealthEndpointTests
{
    private static readonly Uri Health = new("/api/tuners/health", UriKind.Relative);

    private static readonly Uri Settings = new("/api/tuners/health/settings", UriKind.Relative);

    private static readonly string[] Everything =
    [
        DriverCapabilities.TunerLedger,
        DriverCapabilities.DeviceDetection,
    ];

    [Fact]
    public async Task ATypeWhoseServicesHaveAllGoneQuietForLongerThanAllowedIsCalledMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(25));

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal("missing", LevelOf(systems, "isdbSBs"));
        Assert.Equal(0, ServicesOf(systems, "isdbSBs"));
    }

    [Fact]
    public async Task ATypeQuietForLessThanAllowedIsNotYetMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(23));

        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Fact]
    public async Task RaisingHowLongSilenceIsAllowedStopsTheSameSilenceBeingCalledMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(25));

        Assert.Equal("missing", LevelOf(await SystemsAsync(feature), "isdbSBs"));

        using HttpResponseMessage raised = await feature.Client.PutAsJsonAsync(
            Settings,
            new { hoursOfSilence = 48 });

        Assert.Equal(HttpStatusCode.OK, raised.StatusCode);
        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Fact]
    public async Task LoweringHowLongSilenceIsAllowedCallsAShorterSilenceMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(5));

        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));

        using HttpResponseMessage lowered = await feature.Client.PutAsJsonAsync(
            Settings,
            new { hoursOfSilence = 4 });

        Assert.Equal(HttpStatusCode.OK, lowered.StatusCode);
        Assert.Equal("missing", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Fact]
    public async Task ATypeNothingHasEverBeenFoundOnIsUnmeasuredRatherThanMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal("unmeasured", LevelOf(systems, "isdbSBs"));
        Assert.Equal("unmeasured", LevelOf(systems, "isdbSCs110"));
    }

    [Fact]
    public async Task OneSatelliteTunerPutsBothSatelliteTypesInTheAnswerAndNoTerrestrialOne()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal(
            ["isdbSBs", "isdbSCs110"],
            systems.EnumerateArray().Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task ATypeNoConfiguredTunerReceivesIsNotJudgedAtAll()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);
        Quiet(feature, TimeSpan.FromDays(9));

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal(
            ["isdbT"],
            systems.EnumerateArray().Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task TheAnswerCarriesTheWaitCurrentlyInForce()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(24, body.GetProperty("data").GetProperty("hoursOfSilence").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    [InlineData(-1)]
    public async Task AWaitOutsideTheRangeIsRefusedAndLeavesTheOneInForceAlone(int hours)
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        using HttpResponseMessage refused = await feature.Client.PutAsJsonAsync(
            Settings,
            new { hoursOfSilence = hours });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        (HttpStatusCode _, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(24, body.GetProperty("data").GetProperty("hoursOfSilence").GetInt32());
    }

    [Fact]
    public async Task AWaitThatNamesNoNumberAtAllIsRefused()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        using HttpResponseMessage refused = await feature.Client.PutAsJsonAsync(
            Settings,
            new { nothing = true });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheAnswerUnknownRatherThanEmpty()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        using HttpResponseMessage response = await feature.Client.GetAsync(Health);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ATunerTheDriverNeverDescribedIsNamedRatherThanDroppedQuietly()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver =>
            {
                driver.Ledger = new TunerLedgerDto
                {
                    Tuners = [new TunerConfigEntry { DeviceId = "adapter0" }],
                    SavedHash = "saved",
                    LoadedHash = "saved",
                };
                driver.Tuners = [];
            });

        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter0"],
            body.GetProperty("data").GetProperty("undetermined").EnumerateArray()
                .Select(entry => entry.GetString()));
    }

    private static async Task<DriverFeature> StartAsync(TunerKind kind)
        => await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver =>
            {
                driver.Ledger = new TunerLedgerDto
                {
                    Tuners = [new TunerConfigEntry { DeviceId = "adapter0" }],
                    SavedHash = "saved",
                    LoadedHash = "saved",
                };

                driver.Tuners = [new TunerSnapshot("adapter0", kind, TunerState.Idle)];
            });

    private static void Quiet(DriverFeature feature, TimeSpan ago)
    {
        DateTime lastSeenAt = DateTime.UtcNow - ago;

        feature.Candidates.Candidates.Add(CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Bs(15, new TransportStreamId(16_400)),
            observedStreamId: null,
            isSelected: false,
            selectionSource: null,
            selectedAt: null,
            selectionMeasurement: null,
            lastMeasurement: null,
            needsRevalidation: false,
            rotationState: RotationState.NeedsAttention,
            consecutiveFailures: 5,
            nextAttemptAt: null,
            needsAttentionSince: lastSeenAt,
            discoveredAt: lastSeenAt,
            lastSeenAt: lastSeenAt));
    }

    private static async Task<JsonElement> SystemsAsync(DriverFeature feature)
    {
        (HttpStatusCode status, JsonElement body) = await ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("systems").Clone();
    }

    private static string LevelOf(JsonElement systems, string system)
        => Named(systems, system).GetProperty("level").GetString()!;

    private static int ServicesOf(JsonElement systems, string system)
        => Named(systems, system).GetProperty("services").GetInt32();

    private static JsonElement Named(JsonElement systems, string system)
        => systems.EnumerateArray()
            .Single(entry => entry.GetProperty("system").GetString() == system);

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }
}
