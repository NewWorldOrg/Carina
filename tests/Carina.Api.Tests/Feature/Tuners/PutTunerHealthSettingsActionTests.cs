using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.TunerHealthFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[Trait("Category", "Feature")]
public sealed class PutTunerHealthSettingsActionTests
{
    private static readonly Uri Settings = new("/api/tuners/health/settings", UriKind.Relative);

    [Fact]
    public async Task RaisingHowLongSilenceIsAllowedStopsTheSameSilenceBeingCalledMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(25));

        Assert.Equal("missing", LevelOf(await SystemsAsync(feature), "isdbSBs"));
        Assert.Equal(HttpStatusCode.OK, await AllowAsync(feature, 48));
        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Fact]
    public async Task LoweringHowLongSilenceIsAllowedCallsAShorterSilenceMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(5));

        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));
        Assert.Equal(HttpStatusCode.OK, await AllowAsync(feature, 4));
        Assert.Equal("missing", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(719)]
    [InlineData(720)]
    public async Task AWaitInsideTheRangeIsTakenAndComesBackOnTheNextReading(int hours)
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        Assert.Equal(HttpStatusCode.OK, await AllowAsync(feature, hours));

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(hours, body.GetProperty("data").GetProperty("hoursOfSilence").GetInt32());
    }

    [Fact]
    public async Task AWaitOfOneHourCallsAnHourOfSilenceMissing()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.OK, await AllowAsync(feature, 1));
        Assert.Equal("missing", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Fact]
    public async Task AWaitOfThirtyDaysLeavesAWeekOfSilenceMerelySilent()
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Satellite);
        Quiet(feature, TimeSpan.FromDays(7));

        Assert.Equal(HttpStatusCode.OK, await AllowAsync(feature, 720));
        Assert.Equal("silent", LevelOf(await SystemsAsync(feature), "isdbSBs"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    [InlineData(-1)]
    public async Task AWaitOutsideTheRangeIsRefusedAndLeavesTheOneInForceAlone(int hours)
    {
        await using DriverFeature feature = await StartAsync(TunerKind.Terrestrial);

        Assert.Equal(HttpStatusCode.BadRequest, await AllowAsync(feature, hours));

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Health));

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

    private static async Task<HttpStatusCode> AllowAsync(DriverFeature feature, int hours)
    {
        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(
            Settings,
            new { hoursOfSilence = hours });

        return response.StatusCode;
    }
}
