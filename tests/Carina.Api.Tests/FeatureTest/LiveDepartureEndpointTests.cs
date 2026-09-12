using System.Text.Json;

using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveDepartureEndpointTests
{
    [Fact]
    public async Task EveryWayAWireCanEndIsAnsweredForEvenWhenNoWireHasEndedThatWay()
    {
        await using LiveFeature feature = new();

        (_, JsonDocument body) = await feature.GetAsync("/api/live/departures");

        JsonElement departures = body.RootElement.GetProperty("data").GetProperty("departures");

        Assert.Equal(
            Enum.GetValues<LiveDeparture>().Select(Spelled),
            departures.EnumerateArray().Select(counted => counted.GetProperty("departure").GetString()));
        Assert.All(
            departures.EnumerateArray(),
            counted => Assert.Equal(0L, counted.GetProperty("times").GetInt64()));
    }

    [Fact]
    public async Task HowManyWiresEndedEachWayAndWhenTheLastOneDidAreAnswered()
    {
        await using LiveFeature feature = new();

        feature.Departures.Counted(LiveDeparture.ViewerStoppedReading, 7L, LiveFeature.At.AddMinutes(20));

        (_, JsonDocument body) = await feature.GetAsync("/api/live/departures");

        JsonElement counted = Named(body, LiveDeparture.ViewerStoppedReading);

        Assert.Equal(7L, counted.GetProperty("times").GetInt64());
        Assert.Equal(LiveFeature.At.AddMinutes(20), counted.GetProperty("lastAt").GetDateTime());
    }

    [Fact]
    public async Task AWayNoWireHasEndedInSaysNothingAboutWhenItLastHappened()
    {
        await using LiveFeature feature = new();

        feature.Departures.Counted(LiveDeparture.ViewerLeft, 1L, LiveFeature.At);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/departures");

        Assert.Equal(JsonValueKind.Null, Named(body, LiveDeparture.SourceBroke).GetProperty("lastAt").ValueKind);
    }

    [Fact]
    public async Task TheTallySaysWhenItStartedCounting()
    {
        await using LiveFeature feature = new();

        (_, JsonDocument body) = await feature.GetAsync("/api/live/departures");

        Assert.Equal(LiveFeature.At, body.RootElement.GetProperty("data").GetProperty("since").GetDateTime());
    }

    [Fact]
    public async Task TheFieldsAskedForAreAnsweredAndNoOthers()
    {
        await using LiveFeature feature = new();

        (_, JsonDocument body) = await feature.GetAsync("/api/live/departures");

        JsonElement tally = body.RootElement.GetProperty("data");

        Assert.Equal(
            ["since", "departures"],
            tally.EnumerateObject().Select(field => field.Name));
        Assert.Equal(
            ["departure", "times", "lastAt"],
            tally.GetProperty("departures")[0].EnumerateObject().Select(field => field.Name));
    }

    private static string Spelled(LiveDeparture departure)
        => JsonNamingPolicy.CamelCase.ConvertName(departure.ToString());

    private static JsonElement Named(JsonDocument body, LiveDeparture departure)
        => body.RootElement
            .GetProperty("data")
            .GetProperty("departures")
            .EnumerateArray()
            .Single(counted => counted.GetProperty("departure").GetString() == Spelled(departure));
}
