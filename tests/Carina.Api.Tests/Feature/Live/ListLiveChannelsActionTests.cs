using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class ListLiveChannelsActionTests
{
    [Fact]
    public async Task TheChannelsThatCanBeWatchedAreTheTelevisionServicesWithASelectedChannelInRemoteControlOrder()
    {
        await using LiveFeature feature = new();

        feature.Seed(1032, "Second", remoteControlKey: 2);
        feature.Seed(1024, "First", remoteControlKey: 1);
        feature.Seed(1040, "Unselected", remoteControlKey: 3, selected: false);
        feature.Seed(1048, "Radio", remoteControlKey: 4, category: ServiceCategory.Radio);
        feature.Seed(1056, "Data", remoteControlKey: 5, category: ServiceCategory.Data);
        feature.Seed(1064, "Keyless");

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal(["First", "Second", "Keyless"], Names(data));
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(LiveChannelQuery.DefaultPerPage, data.GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task AChannelSaysHowManyAreWatchingItAcrossEveryProfile()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Seed(1032, "Quiet", remoteControlKey: 2);
        feature.Watching(1024, LiveProfile.Hd30, 2);
        feature.Watching(1024, LiveProfile.Hd60, 1);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels");

        JsonElement[] items = [.. body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(3, items[0].GetProperty("viewers").GetInt32());
        Assert.Equal(0, items[1].GetProperty("viewers").GetInt32());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("sessions").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("tuning").ValueKind);
    }

    [Fact]
    public async Task TheFieldsAskedForAreAnsweredAndNoOthers()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(1024, LiveProfile.Hd30, 2, dropped: 28L);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels?fields=sessions&fields=tuning");

        JsonElement item = body.RootElement.GetProperty("data").GetProperty("items")[0];
        JsonElement session = item.GetProperty("sessions")[0];

        Assert.Equal("720p30", session.GetProperty("profile").GetString());
        Assert.Equal(2, session.GetProperty("viewers").GetInt32());
        Assert.Equal(28L, session.GetProperty("dropped").GetInt64());
        Assert.True(session.GetProperty("startup").GetProperty("inProgress").GetBoolean());
        Assert.Equal(27, item.GetProperty("tuning").GetProperty("physicalChannel").GetInt32());
    }

    [Theory]
    [InlineData("sort=name", new[] { "Alpha", "Beta", "Gamma" })]
    [InlineData("sort=name&descending=true", new[] { "Gamma", "Beta", "Alpha" })]
    [InlineData("sort=viewers&descending=true", new[] { "Beta", "Gamma", "Alpha" })]
    [InlineData("sort=remoteControlKey", new[] { "Gamma", "Alpha", "Beta" })]
    public async Task TheSortsOnTheListOrderTheChannels(string query, string[] names)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 2);
        feature.Seed(1032, "Beta", remoteControlKey: 3);
        feature.Seed(1040, "Gamma", remoteControlKey: 1);
        feature.Watching(1032, LiveProfile.Hd30, 3);
        feature.Watching(1040, LiveProfile.Hd30, 1);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels?" + query);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(names, Names(body.RootElement.GetProperty("data")));
    }

    [Theory]
    [InlineData("sort=99")]
    [InlineData("sort=alphabetically")]
    [InlineData("fields=99")]
    [InlineData("fields=everything")]
    [InlineData("fields=sessions&fields=everything")]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("perPage=abc")]
    [InlineData("descending=perhaps")]
    public async Task AValueOffTheListIsRefusedRatherThanIgnored(string query)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/live/channels?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels?perPage=5000");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(LiveChannelQuery.MostPerPage, body.RootElement.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task ChannelsArePagedInTheOrderAsked()
    {
        await using LiveFeature feature = new();

        for (int key = 1; key <= 5; key++)
        {
            feature.Seed(1024 + key, $"Channel {key}", remoteControlKey: key);
        }

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels?perPage=2&page=2");

        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal(["Channel 3", "Channel 4"], Names(data));
        Assert.Equal(5, data.GetProperty("total").GetInt32());
        Assert.Equal(3, data.GetProperty("lastPage").GetInt32());
    }

    private static string[] Names(JsonElement data)
        => [.. data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
}
