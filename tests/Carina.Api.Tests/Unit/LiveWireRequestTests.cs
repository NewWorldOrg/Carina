using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireRequestTests
{
    private static readonly LiveSessionKey Asked = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    public static TheoryData<string, string, string> WhatIsNotAKey =>
        new()
        {
            { "32736", "1024", "hls" },
            { "32736", "1024", "720P30" },
            { "32736", "1024", "" },
            { "", "1024", "720p30" },
            { "32736", "", "720p30" },
            { "thirty", "1024", "720p30" },
            { "32736", "-1", "720p30" },
            { "65536", "1024", "720p30" },
            { "32736", "65536", "720p30" },
            { "0x7fe0", "1024", "720p30" },
            { " 32736", "1024", "720p30" },
        };

    [Fact]
    public void TheThreePartsOfTheQueryMakeTheKey()
    {
        Assert.Equal(Asked, LiveWireRequest.KeyOf(Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"))));
    }

    [Fact]
    public void AQueryThatNamesNoSoundAsksForTheMainOne()
    {
        LiveSessionKey? key = LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30")));

        Assert.Equal(SoundTrack.Main, key!.Sound);
    }

    [Fact]
    public void AQueryNamingTheSecondarySoundAsksForIt()
    {
        LiveSessionKey? key = LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"), ("sound", "secondary")));

        Assert.Equal(SoundTrack.Secondary, key!.Sound);
        Assert.NotEqual(Asked, key);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("Main")]
    [InlineData("1")]
    [InlineData("")]
    public void AQueryNamingASoundThisApplicationDoesNotCarryIsNoKey(string sound)
    {
        Assert.Null(LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"), ("sound", sound))));
    }

    [Fact]
    public void ASoundGivenTwiceIsNoKey()
    {
        QueryCollection query = new(new Dictionary<string, StringValues>
        {
            ["network"] = "32736",
            ["service"] = "1024",
            ["profile"] = "720p30",
            ["sound"] = new StringValues(["main", "secondary"]),
        });

        Assert.Null(LiveWireRequest.KeyOf(query));
    }

    [Fact(DisplayName = "a query that names no format asks for H.264, as every wire did")]
    public void AQueryThatNamesNoFormatAsksForH264()
    {
        LiveSessionKey? key = LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30")));

        Assert.Equal(VideoCodec.H264, key!.Format);
    }

    [Theory(DisplayName = "a query naming a format asks for it, and that is another key")]
    [InlineData("h264", VideoCodec.H264)]
    [InlineData("h265", VideoCodec.H265)]
    [InlineData("av1", VideoCodec.Av1)]
    public void AQueryNamingAFormatAsksForIt(string named, VideoCodec format)
    {
        LiveSessionKey? key = LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"), ("format", named)));

        Assert.Equal(new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30, SoundTrack.Main, format), key);
    }

    [Theory(DisplayName = "a query naming a format off the list is no key")]
    [InlineData("H265")]
    [InlineData("hevc")]
    [InlineData("AV1")]
    [InlineData("vp9")]
    [InlineData("")]
    public void AQueryNamingAFormatOffTheListIsNoKey(string format)
    {
        Assert.Null(LiveWireRequest.KeyOf(
            Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"), ("format", format))));
    }

    [Fact]
    public void AFormatGivenTwiceIsNoKey()
    {
        QueryCollection query = new(new Dictionary<string, StringValues>
        {
            ["network"] = "32736",
            ["service"] = "1024",
            ["profile"] = "720p30",
            ["format"] = new StringValues(["h264", "h265"]),
        });

        Assert.Null(LiveWireRequest.KeyOf(query));
    }

    [Fact]
    public void TheRefusalNamesEveryFormat()
    {
        Assert.All(VideoCodecs.Names, name => Assert.Contains(name, LiveWireRequest.TheKeyThereIs, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WhatIsNotAKey))]
    public void AQueryMissingOrMisspellingAPartIsNoKey(string network, string service, string profile)
    {
        Assert.Null(LiveWireRequest.KeyOf(Query(("network", network), ("service", service), ("profile", profile))));
    }

    [Fact]
    public void APartGivenTwiceIsNoKey()
    {
        QueryCollection query = new(new Dictionary<string, StringValues>
        {
            ["network"] = new StringValues(["32736", "32737"]),
            ["service"] = "1024",
            ["profile"] = "720p30",
        });

        Assert.Null(LiveWireRequest.KeyOf(query));
    }

    [Fact]
    public void AQueryWithNothingInItIsNoKey()
    {
        Assert.Null(LiveWireRequest.KeyOf(QueryCollection.Empty));
    }

    [Fact]
    public void EveryProfileOnTheListIsAKeyAndTheRefusalNamesThemAll()
    {
        foreach (LiveProfile profile in LiveProfile.All)
        {
            Assert.Equal(
                profile,
                LiveWireRequest.KeyOf(Query(("network", "32736"), ("service", "1024"), ("profile", profile.Name)))!.Profile);
            Assert.Contains(profile.Name, LiveWireRequest.TheKeyThereIs, StringComparison.Ordinal);
        }
    }

    private static QueryCollection Query(params (string Name, string Value)[] parts)
        => new(parts.ToDictionary(part => part.Name, part => new StringValues(part.Value)));
}
