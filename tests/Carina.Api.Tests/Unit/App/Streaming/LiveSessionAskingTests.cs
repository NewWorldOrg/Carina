using Carina.Api.Live;
using Carina.Api.Requests;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit.App.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveSessionAskingTests
{
    public static TheoryData<LiveSessionRequest?> WhatAsksForNoSession =>
    [
        null,
        new LiveSessionRequest(),
        new LiveSessionRequest { NetworkId = 32736 },
        new LiveSessionRequest { ServiceId = 1024 },
        new LiveSessionRequest { NetworkId = -1, ServiceId = 1024 },
        new LiveSessionRequest { NetworkId = 65536, ServiceId = 1024 },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = -1 },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 65536 },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024, Profile = "hls" },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024, Profile = "720P30" },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024, Sound = "sub" },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024, Format = "vp9" },
        new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024, Format = "H265" },
    ];

    [Fact]
    public void AChannelAloneAsksForTheMainSoundInH264AndLeavesTheProfileToTheMachine()
    {
        LiveSessionAsking? asked = LiveSessionAsking.Read(new LiveSessionRequest { NetworkId = 32736, ServiceId = 1024 });

        Assert.Equal(
            new LiveSessionAsking(new NetworkId(32736), new ServiceId(1024), null, SoundTrack.Main, VideoCodec.H264),
            asked);
    }

    [Fact]
    public void EverythingNamedIsEverythingAskedFor()
    {
        LiveSessionAsking? asked = LiveSessionAsking.Read(new LiveSessionRequest
        {
            NetworkId = 32736,
            ServiceId = 1024,
            Profile = "1080p60",
            Sound = "secondary",
            Format = "h265",
        });

        Assert.Equal(
            new LiveSessionAsking(
                new NetworkId(32736),
                new ServiceId(1024),
                LiveProfile.FullHd60,
                SoundTrack.Secondary,
                VideoCodec.H265),
            asked);
    }

    [Theory]
    [MemberData(nameof(WhatAsksForNoSession))]
    public void WhatNamesNoWholeChannelOrNamesSomethingThereIsNotAsksForNothing(LiveSessionRequest? request)
    {
        Assert.Null(LiveSessionAsking.Read(request));
    }

    [Fact]
    public void WhatASessionIsAskedWithNamesEveryProfileSoundAndFormatThereIs()
    {
        Assert.All(LiveProfile.All, profile => Assert.Contains(profile.Name, LiveSessionAsking.WhatASessionIsAskedWith, StringComparison.Ordinal));
        Assert.All(SoundTracks.Names, name => Assert.Contains(name, LiveSessionAsking.WhatASessionIsAskedWith, StringComparison.Ordinal));
        Assert.All(VideoCodecs.Names, name => Assert.Contains(name, LiveSessionAsking.WhatASessionIsAskedWith, StringComparison.Ordinal));
    }
}
