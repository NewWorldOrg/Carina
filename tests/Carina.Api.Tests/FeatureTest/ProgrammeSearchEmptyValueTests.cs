using System.Net;

using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProgrammeSearchEmptyValueTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("sort=", HttpStatusCode.OK)]
    [InlineData("descending=", HttpStatusCode.OK)]
    [InlineData("type=", HttpStatusCode.OK)]
    [InlineData("page=", HttpStatusCode.OK)]
    [InlineData("perPage=", HttpStatusCode.OK)]
    [InlineData("from=", HttpStatusCode.OK)]
    [InlineData("to=", HttpStatusCode.OK)]
    [InlineData("exclude=", HttpStatusCode.OK)]
    [InlineData("fields=", HttpStatusCode.BadRequest)]
    [InlineData("genre=", HttpStatusCode.BadRequest)]
    [InlineData("channel=", HttpStatusCode.BadRequest)]
    public async Task AWordGivenWithNoValueAtAllIsReadAsAWordNobodyGave(string word, HttpStatusCode expected)
    {
        await using var feature = new EpgFeature(null, clock: new WoundClock(At));

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/programs/search?keyword=news&{word}");

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task AWordGivenTwiceWhereOnlyOneIsAllowedIsReadFromTheFirstOfThem()
    {
        await using var feature = new EpgFeature(null, clock: new WoundClock(At));

        feature.Programmes.Programmes.Add(Programme(1, "first news"));
        feature.Programmes.Programmes.Add(Programme(2, "other news"));

        (HttpStatusCode status, System.Text.Json.JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=first&keyword=other");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(
            "first news",
            body.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());
    }

    private static Domain.Programmes.Programme Programme(int carried, string name)
        => Domain.Programmes.Programme.Discover(
            new Domain.Programmes.ProgrammeBroadcast(
                new Domain.Programmes.ProgrammeId(
                    new Domain.Channels.NetworkId(4),
                    new Domain.Channels.ServiceId(1049),
                    new Domain.Programmes.EventId(carried)),
                new Domain.Channels.TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                string.Empty,
                false),
            At);
}
