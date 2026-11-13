using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.TestSupport;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.TunerHealthFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[Trait("Category", "Feature")]
public sealed class GetTunerHealthActionTests
{
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

        Assert.Equal(
            ["isdbSBs", "isdbSCs110"],
            (await SystemsAsync(feature)).EnumerateArray()
                .Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task ATypeNoConfiguredTunerReceivesIsNotJudgedAtAll()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);
        Quiet(feature, TimeSpan.FromDays(9));

        Assert.Equal(
            ["isdbT"],
            (await SystemsAsync(feature)).EnumerateArray()
                .Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task ATypeWhoseOnlyTunerHasBeenTurnedOffIsNoLongerJudgedEvenThoughItsServicesWentQuiet()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver =>
            {
                Stocked(
                    driver,
                    [Entry("adapter0", TunerKind.Terrestrial), Entry("adapter1", TunerKind.Satellite)]);

                driver.Tuners =
                [
                    new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
                    new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
                ];
            });
        Quiet(feature, TimeSpan.FromDays(9));

        Assert.Equal(
            ["isdbT"],
            (await SystemsAsync(feature)).EnumerateArray()
                .Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task AMachineWhoseTunersAreAllUndeterminedDoesNotAnswerAllClear()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Unspecified);

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Health));
        JsonElement data = body.GetProperty("data");
        JsonElement systems = data.GetProperty("systems");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(systems.EnumerateArray());
        Assert.Equal(
            ["isdbT", "isdbSBs", "isdbSCs110"],
            systems.EnumerateArray().Select(system => system.GetProperty("system").GetString()));
        Assert.All(
            systems.EnumerateArray(),
            system => Assert.Equal("undetermined", system.GetProperty("level").GetString()));
        Assert.Equal(
            ["adapter0"],
            data.GetProperty("undetermined").EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task ATunerNobodyCouldDescribeDoesNotHideAKnownTypeThatIsFine()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver => Stocked(
                driver,
                [Entry("adapter0", TunerKind.Terrestrial), Entry("adapter1", TunerKind.Unspecified)]));

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal("unmeasured", LevelOf(systems, "isdbT"));
        Assert.Equal("undetermined", LevelOf(systems, "isdbSBs"));
    }

    [Fact]
    public async Task AMachineWhoseEveryTunerIsFaultedIsStillJudgedOnWhatThoseTunersReceive()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver =>
            {
                Stocked(
                    driver,
                    [Entry("adapter0", TunerKind.Terrestrial), Entry("adapter1", TunerKind.Satellite)]);

                driver.Tuners =
                [
                    new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Faulted),
                    new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Faulted),
                ];
            });

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal(
            ["isdbT", "isdbSBs", "isdbSCs110"],
            systems.EnumerateArray().Select(system => system.GetProperty("system").GetString()));
    }

    [Fact]
    public async Task TheAnswerCarriesTheWaitCurrentlyInForce()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(24, body.GetProperty("data").GetProperty("hoursOfSilence").GetInt32());
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheAnswerUnknownRatherThanEmpty()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        using HttpResponseMessage response = await feature.Client.GetAsync(Health);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ATunerSavedButNotYetLoadedByTheDriverIsStillJudgedOnItsOwnType()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver =>
            {
                Stocked(driver, [Entry("adapter0", TunerKind.Satellite)]);
                driver.Tuners = [];
            });

        JsonElement systems = await SystemsAsync(feature);

        Assert.Equal(
            ["isdbSBs", "isdbSCs110"],
            systems.EnumerateArray().Select(system => system.GetProperty("system").GetString()));
    }

    private static int ServicesOf(JsonElement systems, string system)
        => Named(systems, system).GetProperty("services").GetInt32();
}
