using System.Reflection;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveProfileTests
{
    public static TheoryData<string> WhatIsNotAProfile =>
    [
        "1080p90",
        "720P30",
        " 720p30",
        "720p30 ",
        "720p",
        "hls",
        string.Empty,
    ];

    [Fact]
    public void AProfileCannotBeMadeFromOutsideTheList()
    {
        Assert.Empty(typeof(LiveProfile).GetConstructors());
    }

    [Theory]
    [MemberData(nameof(WhatIsNotAProfile))]
    public void ANameThatIsNotOnTheListIsNotAProfile(string name)
    {
        Assert.Null(LiveProfile.Find(name));
    }

    [Fact]
    public void NoNameAtAllIsNotAProfile()
    {
        Assert.Null(LiveProfile.Find(null));
    }

    [Fact]
    public void EveryListedNameFindsTheProfileItNames()
    {
        foreach (LiveProfile profile in LiveProfile.All)
        {
            Assert.Same(profile, LiveProfile.Find(profile.Name));
        }
    }

    [Fact]
    public void TheListIsEveryProfileThereIs()
    {
        LiveProfile[] declared =
        [
            .. typeof(LiveProfile)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(LiveProfile))
                .Select(field => (LiveProfile)field.GetValue(null)!),
        ];

        Assert.Equal(declared.Order(Comparer<LiveProfile>.Create(ByName)), LiveProfile.All.Order(Comparer<LiveProfile>.Create(ByName)));
    }

    [Fact]
    public void TheProfilesAreTheFourThatAreOffered()
    {
        Assert.Equal(
            ["1080p60", "1080p30", "720p60", "720p30"],
            LiveProfile.All.Select(profile => profile.Name));
    }

    [Fact]
    public void AProfileFoundTwiceIsTheSameProfile()
    {
        Assert.Equal(LiveProfile.Hd30, LiveProfile.Find("720p30"));
        Assert.Equal(LiveProfile.Hd30.GetHashCode(), LiveProfile.Find("720p30")!.GetHashCode());
    }

    [Fact]
    public void ASessionKeyTellsTheProfilesApart()
    {
        HashSet<(string Kind, int Channel, LiveProfile Profile)> keys =
        [
            .. LiveProfile.All.Select(profile => ("gr", 27, profile)),
        ];

        Assert.Equal(LiveProfile.All.Count, keys.Count);
        Assert.Contains(("gr", 27, LiveProfile.Find("720p30")!), keys);
        Assert.DoesNotContain(("gr", 28, LiveProfile.Hd30), keys);
    }

    [Fact]
    public void TheFrameRateAloneSplitsASession()
    {
        Assert.Equal(LiveProfile.Hd60.Size, LiveProfile.Hd30.Size);
        Assert.NotEqual(LiveProfile.Hd60, LiveProfile.Hd30);
        Assert.NotEqual(LiveProfile.Hd60.Rate, LiveProfile.Hd30.Rate);
    }

    [Fact]
    public void EveryProfileIsTheOneCodecABrowserCanBeGiven()
    {
        Assert.All(LiveProfile.All, profile => Assert.Equal(VideoCodec.H264, profile.Codec));
    }

    [Fact]
    public void EveryProfileIsSixteenByNineInSquarePixels()
    {
        Assert.All(LiveProfile.All, profile => Assert.Equal(profile.Size.Height * 16, profile.Size.Width * 9));
    }

    [Fact]
    public void EveryFrameRateIsOneTheBroadcastCarries()
    {
        Assert.All(LiveProfile.All, profile => Assert.Equal(1001, profile.Rate.Denominator));
        Assert.All(LiveProfile.All, profile => Assert.Contains(profile.Rate.Numerator, (int[])[30000, 60000]));
    }

    [Fact]
    public void NoProfileCapsAboveTheMultiplexItIsMadeFrom()
    {
        Assert.All(LiveProfile.All, profile => Assert.InRange(profile.SoftwareRateControl.KilobitsPerSecond, 1, 10_000));
    }

    [Fact]
    public void ACoarserPictureIsNotGivenMoreRoomThanAFinerOne()
    {
        Assert.True(LiveProfile.FullHd60.SoftwareRateControl.KilobitsPerSecond
            > LiveProfile.FullHd30.SoftwareRateControl.KilobitsPerSecond);
        Assert.True(LiveProfile.FullHd30.SoftwareRateControl.KilobitsPerSecond
            > LiveProfile.Hd60.SoftwareRateControl.KilobitsPerSecond);
        Assert.True(LiveProfile.Hd60.SoftwareRateControl.KilobitsPerSecond
            > LiveProfile.Hd30.SoftwareRateControl.KilobitsPerSecond);
    }

    [Theory]
    [InlineData("1080p60", 1920, 1080, 60000, 9000)]
    [InlineData("1080p30", 1920, 1080, 30000, 6000)]
    [InlineData("720p60", 1280, 720, 60000, 4500)]
    [InlineData("720p30", 1280, 720, 30000, 3000)]
    public void AProfileIsTheValuesItWasListedWith(
        string name,
        int width,
        int height,
        int frames,
        int kilobitsPerSecond)
    {
        LiveProfile? profile = LiveProfile.Find(name);

        Assert.NotNull(profile);
        Assert.Equal(new VideoSize(width, height), profile.Size);
        Assert.Equal(frames, profile.Rate.Numerator);
        Assert.Equal(kilobitsPerSecond, profile.SoftwareRateControl.KilobitsPerSecond);
        Assert.Equal(24, profile.VaapiRateControl.Quantiser);
    }

    [Fact]
    public void TheOnlyRateControlVaapiIsHandedIsAQuantiser()
    {
        PropertyInfo? rateControl = typeof(LiveProfile).GetProperty(nameof(LiveProfile.VaapiRateControl));

        Assert.NotNull(rateControl);
        Assert.Equal(typeof(ConstantQuantiser), rateControl.PropertyType);
    }

    [Fact]
    public void AProfileNamesItself()
    {
        Assert.All(LiveProfile.All, profile => Assert.Equal(profile.Name, profile.ToString()));
    }

    private static int ByName(LiveProfile? left, LiveProfile? right)
        => string.CompareOrdinal(left?.Name, right?.Name);
}
