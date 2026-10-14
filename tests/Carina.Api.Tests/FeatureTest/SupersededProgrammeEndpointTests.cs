using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SupersededProgrammeEndpointTests
{
    private static readonly DateTime Start = new(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

    private const string TheDay = "from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z";

    [Fact]
    public async Task TheGuideDrawsTheReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs?type=isdbT&{TheDay}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["4-1049-1", "4-1049-101", "4-1049-102"], Ids(body.GetProperty("data").GetProperty("programmes")));
    }

    [Fact]
    public async Task TheProgrammeOnAirAndTheOneAfterItAreTheReplacementAndWhatFollowsIt()
    {
        await using EpgFeature feature = Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs?type=isdbT&from=2026-08-18T06:10:00Z&to=2026-08-18T09:10:00Z");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["4-1049-101", "4-1049-102"], Ids(body.GetProperty("data").GetProperty("programmes")));
    }

    [Fact]
    public async Task ASearchFindsTheReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs/search?keyword=番組&{TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(
            ["前の番組", "差し替えの番組", "次の番組"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? string.Empty));
    }

    [Fact]
    public async Task AProgrammeTheReplacementTookThePlaceOfHasNoDetailWhileTheReplacementHasOne()
    {
        await using EpgFeature feature = Replaced();

        (HttpStatusCode replaced, _) = await feature.GetAsync("/api/programs/4-1049-6");
        (HttpStatusCode standing, JsonElement body) = await feature.GetAsync("/api/programs/4-1049-101");

        Assert.Equal(HttpStatusCode.NotFound, replaced);
        Assert.Equal(HttpStatusCode.OK, standing);
        Assert.Equal("差し替えの番組", body.GetProperty("data").GetProperty("name").GetString());
    }

    private static EpgFeature Replaced()
    {
        var feature = new EpgFeature(
        [
            new BroadcastStream(
                new NetworkId(4),
                new TransportStreamId(32_736),
                TuningParameters.Terrestrial(22),
                [new ServiceId(1049)]),
        ]);

        feature.Programmes.Programmes.AddRange(ReplacedSchedule.On(4, 1049, Start));

        return feature;
    }

    private static string[] Ids(JsonElement programmes)
        => [.. programmes.EnumerateArray().Select(programme => programme.GetProperty("id").GetString() ?? string.Empty)];
}
