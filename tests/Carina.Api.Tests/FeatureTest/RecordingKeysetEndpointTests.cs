using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingKeysetEndpointTests
{
    private const string Newest = "/api/recordings?sort=startedAt&descending=true&perPage=2";

    [Fact(DisplayName = "D-11: following next reaches every recording once, newest first")]
    public async Task FollowingNextReachesEveryRecordingOnceNewestFirst()
    {
        await using var feature = new RecordingFeature();
        List<Recording> held =
        [
            .. Enumerable.Range(1, 5).Select(at => feature.Held(eventId: at, startedAt: RecordingFeature.Noon.AddMinutes(at % 3))),
        ];

        List<string> walked = [];
        string path = Newest;

        while (true)
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);
            JsonElement data = body.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(5, data.GetProperty("total").GetInt32());
            walked.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));

            if (data.GetProperty("next").GetString() is not { } next)
            {
                break;
            }

            path = $"{Newest}&after={next}";
        }

        Assert.Equal(5, walked.Count);
        Assert.Equal(held.Select(recording => recording.Id.Wire).Order(StringComparer.Ordinal), walked.Order(StringComparer.Ordinal));
        Assert.Equal(
            held.OrderByDescending(recording => recording.StartedAtActual).Select(recording => recording.StartedAtActual),
            walked.Select(id => held.Single(recording => recording.Id.Wire == id).StartedAtActual));
    }

    [Fact(DisplayName = "D-11: the last page names no next")]
    public async Task TheLastPageNamesNoNext()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Newest);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("next").ValueKind);
    }

    [Fact(DisplayName = "D-11: a page number and a position asked for at once are refused")]
    public async Task APageNumberAndAPositionAskedForAtOnceAreRefused()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);
        feature.Held(eventId: 2, startedAt: RecordingFeature.Noon.AddMinutes(1));
        feature.Held(eventId: 3, startedAt: RecordingFeature.Noon.AddMinutes(2));
        string next = (await feature.GetAsync(Newest)).Body.GetProperty("data").GetProperty("next").GetString()!;

        (HttpStatusCode status, _) = await feature.GetAsync($"{Newest}&after={next}&page=2");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Theory(DisplayName = "D-11: a position that is not one this endpoint wrote, or was written for another order, is refused")]
    [InlineData("&after=not-a-position")]
    [InlineData("&after=written-for-oldest-first")]
    public async Task APositionThatIsNotOneThisEndpointWroteIsRefused(string asked)
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);
        feature.Held(eventId: 2, startedAt: RecordingFeature.Noon.AddMinutes(1));
        feature.Held(eventId: 3, startedAt: RecordingFeature.Noon.AddMinutes(2));
        string oldestFirst = (await feature.GetAsync("/api/recordings?sort=startedAt&descending=false&perPage=2"))
            .Body.GetProperty("data").GetProperty("next").GetString()!;

        (HttpStatusCode status, _) = await feature.GetAsync(
            Newest + asked.Replace("written-for-oldest-first", oldestFirst, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
