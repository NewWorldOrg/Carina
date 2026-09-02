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
