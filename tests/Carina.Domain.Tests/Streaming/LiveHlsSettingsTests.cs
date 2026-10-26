using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveHlsSettingsTests
{
    [Fact(DisplayName = "BR-PV-005: by default a key nobody asks with for thirty seconds is let go of")]
    public void ByDefaultAKeyNobodyAsksWithForThirtySecondsIsLetGoOf()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new LiveHlsSettings().Idle);
    }

    [Fact(DisplayName = "BR-PD-022: by default six segments are held and a segment closes at the first keyframe a second and a half in")]
    public void ByDefaultSixSegmentsAreHeldAndASegmentClosesAtTheFirstKeyframeASecondAndAHalfIn()
    {
        LiveHlsSettings settings = new();

        Assert.Equal(6, settings.SegmentsHeld);
        Assert.Equal(TimeSpan.FromSeconds(1.5), settings.ShortestSegment);
    }

    [Fact]
    public void ByDefaultTheFirstSegmentIsWaitedForAsLongAsASessionIsWaitedForToBeRaised()
    {
        Assert.Equal(new LiveSessionSettings().LongestRaise, new LiveHlsSettings().LongestWait);
    }

    [Fact]
    public void ByDefaultThirtyTwoKeysAreHeldAtOnceAndEightOfThemForOneWatcher()
    {
        LiveHlsSettings settings = new();

        Assert.Equal(32, settings.MostHeldAtOnce);
        Assert.Equal(8, settings.MostHeldPerWatcher);
    }

    [Fact]
    public void AKeyLettingGoTheMomentItIsMadeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { Idle = TimeSpan.Zero });
    }

    [Fact]
    public void HoldingNoSegmentIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { SegmentsHeld = 0 });
    }

    [Fact]
    public void ASegmentOfNoLengthIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { ShortestSegment = TimeSpan.Zero });
    }

    [Fact]
    public void WaitingNoTimeForTheFirstSegmentIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { LongestWait = TimeSpan.Zero });
    }

    [Fact]
    public void HoldingNoKeyAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { MostHeldAtOnce = 0 });
    }

    [Fact]
    public void HoldingNoKeyForAWatcherIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveHlsSettings { MostHeldPerWatcher = 0 });
    }

    [Fact]
    public void WhatIsSetIsKept()
    {
        LiveHlsSettings settings = new()
        {
            Idle = TimeSpan.FromSeconds(3),
            SegmentsHeld = 2,
            ShortestSegment = TimeSpan.FromSeconds(1),
            LongestWait = TimeSpan.FromSeconds(4),
            MostHeldAtOnce = 5,
            MostHeldPerWatcher = 2,
        };

        Assert.Equal(TimeSpan.FromSeconds(3), settings.Idle);
        Assert.Equal(2, settings.SegmentsHeld);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.ShortestSegment);
        Assert.Equal(TimeSpan.FromSeconds(4), settings.LongestWait);
        Assert.Equal(5, settings.MostHeldAtOnce);
        Assert.Equal(2, settings.MostHeldPerWatcher);
    }
}
