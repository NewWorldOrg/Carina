using Carina.Domain.Captions;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class CaptionOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansNoCaptionsKeptAndTheDefaultsBehindThem()
    {
        CaptionSettings read = Read();

        Assert.False(read.KeepsAnything);
        Assert.Null(read.WrittenTo);
        Assert.Equal(TimeSpan.FromMinutes(2), read.BeforeFirstPass);
        Assert.Equal(TimeSpan.FromMinutes(5), read.BetweenPasses);
        Assert.Equal(TimeSpan.FromHours(1), read.LongestTranscription);
        Assert.Equal(4, read.AtMostAPass);
        Assert.Equal(3, CaptionSettings.TriesAtMost);
    }

    [Fact]
    public void EverySettingReachesTheThingThatUsesIt()
    {
        CaptionSettings read = Read(
            ("Captions:WrittenTo", "/srv/captions"),
            ("Captions:BeforeFirstPass", "00:00:10"),
            ("Captions:BetweenPasses", "00:20:00"),
            ("Captions:LongestTranscription", "02:00:00"),
            ("Captions:AtMostAPass", "16"));

        Assert.True(read.KeepsAnything);
        Assert.Equal("/srv/captions", read.WrittenTo);
        Assert.Equal(TimeSpan.FromSeconds(10), read.BeforeFirstPass);
        Assert.Equal(TimeSpan.FromMinutes(20), read.BetweenPasses);
        Assert.Equal(TimeSpan.FromHours(2), read.LongestTranscription);
        Assert.Equal(16, read.AtMostAPass);
    }

    [Theory]
    [InlineData("Captions:WrittenTo", "captions")]
    [InlineData("Captions:BeforeFirstPass", "soon")]
    [InlineData("Captions:BetweenPasses", "00:00:00")]
    [InlineData("Captions:LongestTranscription", "-00:01:00")]
    [InlineData("Captions:AtMostAPass", "0")]
    [InlineData("Captions:AtMostAPass", "many")]
    public void ASettingThatCannotBeUsedStopsTheProcessNamingIt(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.False(new CaptionValidation().Validate(null, Options((key, value))).Succeeded);
    }

    [Fact]
    public void ARecordIsKeptUnderTheRecordingsOwnNameOnTheShelf()
    {
        Domain.Recordings.RecordingId id = Domain.Recordings.RecordingId.New();

        Assert.Equal($"/srv/captions/{id.Wire}.captions", Read(("Captions:WrittenTo", "/srv/captions")).PathOf(id));
        Assert.Null(Read().PathOf(id));
    }

    private static CaptionSettings Read(params (string Key, string Value)[] settings) => Options(settings).Read();

    private static CaptionOptions Options(params (string Key, string Value)[] settings)
    {
        CaptionOptions options = new();
        options.ReadFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());

        return options;
    }
}
