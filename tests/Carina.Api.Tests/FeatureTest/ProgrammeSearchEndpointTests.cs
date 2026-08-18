using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProgrammeSearchEndpointTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AKeywordBringsBackWhatCarriesIt()
    {
        await using var feature = new EpgFeature();

        feature.Programmes.Programmes.Add(Programme(1, "ニュース7"));
        feature.Programmes.Programmes.Add(Programme(2, "天気予報"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=ニュース");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("ニュース7", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AKeywordOfOneLetterIsRefusedBeforeItReachesTheStore()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=あ");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ASearchWithNoKeywordIsRefused()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeBeyondTheCeilingComesBackAtTheCeiling()
    {
        await using var feature = new EpgFeature();

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=news&perPage=100000");

        Assert.Equal(
            ProgrammeSearch.MostPerPage,
            body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task ASpanLongerThanTheCeilingIsRefused()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&from=2026-01-01T00:00:00Z&to=2026-06-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ASortNobodyDefinedIsRefusedRatherThanPassedToTheStore()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&sort=name;DROP TABLE programme");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    private static Programme Programme(int carried, string name)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                string.Empty,
                false),
            At);
}
