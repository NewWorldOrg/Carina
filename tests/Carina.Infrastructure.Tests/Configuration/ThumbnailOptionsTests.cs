using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class ThumbnailOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansNoPicturesAndTheDefaultsBehindThem()
    {
        ThumbnailSettings read = Read();

        Assert.False(read.DrawsAnything);
        Assert.Null(read.WrittenTo);
        Assert.Equal("ffmpeg", read.Programme);
        Assert.Equal(TimeSpan.FromMinutes(1), read.BeforeFirstPass);
        Assert.Equal(TimeSpan.FromMinutes(5), read.BetweenPasses);
        Assert.Equal(TimeSpan.FromSeconds(120), read.NoLaterThan);
        Assert.Equal(3, read.OneOverAShareOf);
        Assert.Equal(TimeSpan.FromSeconds(30), read.LongestRender);
        Assert.Equal(8, read.AtMostAPass);
        Assert.Equal(960, read.Width);
    }

    [Fact]
    public void EverySettingReachesTheThingThatUsesIt()
    {
        ThumbnailSettings read = Read(
            ("Thumbnails:WrittenTo", "/srv/thumbnails"),
            ("Thumbnails:Programme", "/usr/bin/ffmpeg"),
            ("Thumbnails:BeforeFirstPass", "00:00:10"),
            ("Thumbnails:BetweenPasses", "00:20:00"),
            ("Thumbnails:NoLaterThan", "00:00:45"),
            ("Thumbnails:OneOverAShareOf", "4"),
            ("Thumbnails:LongestRender", "00:01:00"),
            ("Thumbnails:AtMostAPass", "32"),
            ("Thumbnails:Width", "1280"));

        Assert.Equal("/srv/thumbnails", read.WrittenTo);
        Assert.Equal("/usr/bin/ffmpeg", read.Programme);
        Assert.Equal(TimeSpan.FromSeconds(10), read.BeforeFirstPass);
        Assert.Equal(TimeSpan.FromMinutes(20), read.BetweenPasses);
        Assert.Equal(TimeSpan.FromSeconds(45), read.NoLaterThan);
        Assert.Equal(4, read.OneOverAShareOf);
        Assert.Equal(TimeSpan.FromMinutes(1), read.LongestRender);
        Assert.Equal(32, read.AtMostAPass);
        Assert.Equal(1280, read.Width);
        Assert.True(read.DrawsAnything);
    }

    [Fact]
    public void TheSmallestSettingsThatStillMeanSomethingAreAccepted()
    {
        ThumbnailSettings read = Read(
            ("Thumbnails:OneOverAShareOf", "1"),
            ("Thumbnails:AtMostAPass", "1"),
            ("Thumbnails:Width", "2"),
            ("Thumbnails:BeforeFirstPass", "00:00:00.001"),
            ("Thumbnails:NoLaterThan", "00:00:00.001"));

        Assert.Equal(1, read.OneOverAShareOf);
        Assert.Equal(1, read.AtMostAPass);
        Assert.Equal(2, read.Width);
        Assert.Equal(TimeSpan.FromMilliseconds(1), read.BeforeFirstPass);
        Assert.Equal(TimeSpan.FromMilliseconds(1), read.NoLaterThan);
    }

    [Theory]
    [InlineData("Thumbnails:WrittenTo", "srv/thumbnails")]
    [InlineData("Thumbnails:Programme", " ffmpeg")]
    [InlineData("Thumbnails:BeforeFirstPass", "soon")]
    [InlineData("Thumbnails:BetweenPasses", "00:00:00")]
    [InlineData("Thumbnails:NoLaterThan", "-00:00:01")]
    [InlineData("Thumbnails:OneOverAShareOf", "0")]
    [InlineData("Thumbnails:OneOverAShareOf", "half")]
    [InlineData("Thumbnails:LongestRender", "00:00:00")]
    [InlineData("Thumbnails:AtMostAPass", "0")]
    [InlineData("Thumbnails:Width", "961")]
    [InlineData("Thumbnails:Width", "0")]
    public void ASettingNothingCouldDoIsRefusedByName(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Equal(key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..], refusal.ParamName);
    }

    [Fact]
    public void TheSameRefusalIsWhatStopsTheProcessStarting()
    {
        var options = new ThumbnailOptions();
        options.ReadFrom(Configuration(("Thumbnails:Width", "961")));

        Assert.True(new ThumbnailValidation().Validate(null, options).Failed);
        Assert.True(new ThumbnailValidation().Validate(null, new ThumbnailOptions()).Succeeded);
    }

    [Fact]
    public void ValidatingNothingIsRefused()
        => Assert.Throws<ArgumentNullException>(() => new ThumbnailValidation().Validate(null, null!));

    private static ThumbnailSettings Read(params (string Key, string Value)[] settings)
    {
        var options = new ThumbnailOptions();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
