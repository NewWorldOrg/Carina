using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Streaming;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class ListLiveProfilesActionTests
{
    [Fact]
    public async Task AMachineWithoutACardOffersTheProfileItsProcessorCanHoldRealTimeOn()
    {
        await using LiveFeature feature = new(LiveEncoder.Software);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        JsonElement[] profiles = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal(
            "720p30",
            profiles.Single(profile => profile.GetProperty("unasked").GetBoolean()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task AMachineWithACardOffersEveryLineAndEveryField()
    {
        await using LiveFeature feature = new(LiveEncoder.Vaapi);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        JsonElement[] profiles = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal(
            "1080p60",
            profiles.Single(profile => profile.GetProperty("unasked").GetBoolean()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheProfilesAreTheOnesTheWireAcceptsAndNothingElse()
    {
        await using LiveFeature feature = new();

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement[] profiles = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal(
            LiveProfile.All.Select(profile => profile.Name),
            profiles.Select(profile => profile.GetProperty("name").GetString()));
        Assert.All(profiles, profile => Assert.NotNull(LiveProfile.Find(profile.GetProperty("name").GetString())));

        JsonElement hd30 = profiles.Single(profile => profile.GetProperty("name").GetString() == "720p30");

        Assert.Equal("h264", hd30.GetProperty("codec").GetString());
        Assert.Equal(1280, hd30.GetProperty("width").GetInt32());
        Assert.Equal(720, hd30.GetProperty("height").GetInt32());
        Assert.Equal(30000, hd30.GetProperty("frameRate").GetProperty("numerator").GetInt32());
        Assert.Equal(1001, hd30.GetProperty("frameRate").GetProperty("denominator").GetInt32());
        Assert.Equal(3000, hd30.GetProperty("softwareKilobitsPerSecond").GetInt32());
        Assert.Equal(24, hd30.GetProperty("vaapiQuantiser").GetInt32());
    }

    [Fact(DisplayName = "a machine that transcodes on its processor offers every profile in H.264 alone")]
    public async Task AMachineThatTranscodesOnItsProcessorOffersEveryProfileInH264Alone()
    {
        await using LiveFeature feature = new(LiveEncoder.Software);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.All(
            body.RootElement.GetProperty("data").EnumerateArray(),
            profile => Assert.Equal(["h264"], profile.GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
    }

    [Fact(DisplayName = "a machine whose card encodes H.265 and AV1 offers every profile in all three formats, H.264 first")]
    public async Task AMachineWhoseCardEncodesH265AndAv1OffersEveryProfileInAllThreeFormats()
    {
        await using LiveFeature feature = new(LiveEncoder.Vaapi, [VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1]);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        JsonElement[] profiles = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal(LiveProfile.All.Count, profiles.Length);
        Assert.All(
            profiles,
            profile => Assert.Equal(
                ["h264", "h265", "av1"],
                profile.GetProperty("formats").EnumerateArray().Select(format => format.GetString())));
        Assert.All(profiles, profile => Assert.Equal("h264", profile.GetProperty("codec").GetString()));
    }
}
