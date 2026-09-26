using Carina.Domain.Encodings;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class EncodingOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansWorkIsWrittenBesideTheArtefact()
    {
        Assert.Null(Read().WorkedIn);
    }

    [Fact]
    public void NothingConfiguredMeansThisProcessHoldsNoRootToEncodeInto()
    {
        EncodeSettings settings = Read();

        Assert.Empty(settings.OutputRoots);
        Assert.False(settings.HoldsAnyRoot);
    }

    [Fact]
    public void TheRootsThisProcessHoldsAreReadInTheOrderTheyWereWritten()
    {
        EncodeSettings settings = Read(("Encodings:OutputRoots", " encodes=/srv/encodes ; spare=/mnt/spare "));

        Assert.Equal(["encodes", "spare"], settings.OutputRoots.Select(held => held.Root.Value));
        Assert.Equal(["/srv/encodes", "/mnt/spare"], settings.OutputRoots.Select(held => held.Path));
        Assert.True(settings.HoldsAnyRoot);
    }

    [Theory]
    [InlineData("encodes")]
    [InlineData("encodes=srv/encodes")]
    [InlineData("../etc=/srv/encodes")]
    [InlineData("encodes=/srv/one;encodes=/srv/two")]
    public void ARootThatCannotBeHeldIsRefusedNamingTheSetting(string written)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(("Encodings:OutputRoots", written)));

        Assert.Contains("Encodings:OutputRoots", refusal.Message, StringComparison.Ordinal);
        Assert.True(new EncodingValidation().Validate(null, new EncodingOptions { OutputRoots = written }).Failed);
    }

    [Fact]
    public void AWorkingDirectoryReachesTheThingThatUsesIt()
    {
        Assert.Equal("/srv/encoding", Read(("Encodings:WorkedIn", "/srv/encoding")).WorkedIn);
    }

    [Theory]
    [InlineData("srv/encoding")]
    [InlineData("./encoding")]
    public void AWorkingDirectoryIsAbsoluteOrItIsRefusedNamingTheSetting(string path)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(("Encodings:WorkedIn", path)));

        Assert.Contains("Encodings:WorkedIn", refusal.Message, StringComparison.Ordinal);

        var options = new EncodingOptions { WorkedIn = path };
        ValidateOptionsResult validated = new EncodingValidation().Validate(null, options);

        Assert.True(validated.Failed);
    }

    [Fact(DisplayName = "a recording that ends is queued for encoding unless this machine was told otherwise")]
    public void ARecordingThatEndsIsQueuedUnlessThisMachineWasToldOtherwise()
    {
        Assert.True(Read().Automatically);
        Assert.True(Read(("Encodings:Automatically", "true")).Automatically);
        Assert.False(Read(("Encodings:Automatically", "false")).Automatically);
    }

    [Fact]
    public void NothingConfiguredAsksForTheProcessorAndGivesAJobThreeAttempts()
    {
        EncodeSettings settings = Read();

        Assert.Equal(EncodeEncoder.Software, settings.Prefer);
        Assert.Equal(2, settings.MostCores);
        Assert.Equal(3, settings.MostAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.BetweenLooks);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.StalledAfter);
    }

    [Fact]
    public void EachRunSettingReachesTheThingThatUsesIt()
    {
        EncodeSettings settings = Read(
            ("Encodings:Prefer", "vaapi"),
            ("Encodings:MostCores", "4"),
            ("Encodings:MostAttempts", "5"),
            ("Encodings:BetweenLooks", "00:01:00"),
            ("Encodings:StalledAfter", "00:20:00"));

        Assert.Equal(EncodeEncoder.Vaapi, settings.Prefer);
        Assert.Equal(4, settings.MostCores);
        Assert.Equal(5, settings.MostAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.BetweenLooks);
        Assert.Equal(TimeSpan.FromMinutes(20), settings.StalledAfter);
    }

    [Theory]
    [InlineData("Encodings:Automatically", "yes")]
    [InlineData("Encodings:Prefer", "quicksync")]
    [InlineData("Encodings:Prefer", "3")]
    [InlineData("Encodings:MostCores", "0")]
    [InlineData("Encodings:MostCores", "two")]
    [InlineData("Encodings:MostAttempts", "0")]
    [InlineData("Encodings:MostAttempts", "three")]
    [InlineData("Encodings:BetweenLooks", "00:00:00")]
    [InlineData("Encodings:BetweenLooks", "-00:00:01")]
    [InlineData("Encodings:StalledAfter", "soon")]
    public void ARunSettingThatCannotBeReadIsRefusedNamingTheSetting(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingConfiguredLooksForTheBreaksOnTheFifteenSecondGrid()
    {
        ChapterSettings chapters = Read().Chapters;

        Assert.True(chapters.Marked);
        Assert.True(chapters.Watermark);
        Assert.Equal(-50, chapters.Noise);
        Assert.Equal(TimeSpan.FromMilliseconds(150), chapters.ShortestSilence);
        Assert.Equal(0.30, chapters.Scene);
        Assert.Equal(TimeSpan.FromSeconds(15), chapters.Grid);
        Assert.Equal(TimeSpan.FromSeconds(1), chapters.GridTolerance);
        Assert.Equal(0.5, chapters.MostBreakShare);
        Assert.Equal(40, chapters.MostChapters);
    }

    [Fact]
    public void EachChapterSettingReachesTheThingThatUsesIt()
    {
        ChapterSettings chapters = Read(
            ("Encodings:Chapters:Marked", "false"),
            ("Encodings:Chapters:Watermark", "false"),
            ("Encodings:Chapters:Noise", "-42"),
            ("Encodings:Chapters:ShortestSilence", "00:00:00.400"),
            ("Encodings:Chapters:Scene", "0.45"),
            ("Encodings:Chapters:Grid", "00:00:30"),
            ("Encodings:Chapters:GridTolerance", "00:00:02"),
            ("Encodings:Chapters:MostBreakShare", "0.75"),
            ("Encodings:Chapters:MostChapters", "12")).Chapters;

        Assert.False(chapters.Marked);
        Assert.False(chapters.Watermark);
        Assert.Equal(-42, chapters.Noise);
        Assert.Equal(TimeSpan.FromMilliseconds(400), chapters.ShortestSilence);
        Assert.Equal(0.45, chapters.Scene);
        Assert.Equal(TimeSpan.FromSeconds(30), chapters.Grid);
        Assert.Equal(TimeSpan.FromSeconds(2), chapters.GridTolerance);
        Assert.Equal(0.75, chapters.MostBreakShare);
        Assert.Equal(12, chapters.MostChapters);
    }

    [Theory]
    [InlineData("Encodings:Chapters:Marked", "true")]
    [InlineData("Encodings:Chapters:Watermark", "false")]
    [InlineData("Encodings:Chapters:Noise", "-100")]
    [InlineData("Encodings:Chapters:Noise", "-1")]
    [InlineData("Encodings:Chapters:ShortestSilence", "00:00:00.001")]
    [InlineData("Encodings:Chapters:Scene", "0.001")]
    [InlineData("Encodings:Chapters:Scene", "1")]
    [InlineData("Encodings:Chapters:Grid", "00:00:03")]
    [InlineData("Encodings:Chapters:GridTolerance", "00:00:07.499")]
    [InlineData("Encodings:Chapters:MostBreakShare", "0.001")]
    [InlineData("Encodings:Chapters:MostBreakShare", "1")]
    [InlineData("Encodings:Chapters:MostChapters", "1")]
    public void AChapterSettingOnTheAllowedSideOfItsBoundIsRead(string key, string value)
    {
        Assert.True(new EncodingValidation().Validate(null, Written(key, value)).Succeeded);
    }

    [Theory]
    [InlineData("Encodings:Chapters:Marked", "sometimes")]
    [InlineData("Encodings:Chapters:Watermark", "in the corner")]
    [InlineData("Encodings:Chapters:Noise", "-101")]
    [InlineData("Encodings:Chapters:Noise", "+0")]
    [InlineData("Encodings:Chapters:Noise", "quiet")]
    [InlineData("Encodings:Chapters:ShortestSilence", "00:00:00")]
    [InlineData("Encodings:Chapters:ShortestSilence", "-00:00:01")]
    [InlineData("Encodings:Chapters:Scene", "0.0")]
    [InlineData("Encodings:Chapters:Scene", "1.001")]
    [InlineData("Encodings:Chapters:Scene", "a lot")]
    [InlineData("Encodings:Chapters:Grid", "00:00:00")]
    [InlineData("Encodings:Chapters:Grid", "-00:00:15")]
    [InlineData("Encodings:Chapters:GridTolerance", "00:00:00")]
    [InlineData("Encodings:Chapters:GridTolerance", "00:00:07.500")]
    [InlineData("Encodings:Chapters:MostBreakShare", "0.0")]
    [InlineData("Encodings:Chapters:MostBreakShare", "1.001")]
    [InlineData("Encodings:Chapters:MostBreakShare", "half")]
    [InlineData("Encodings:Chapters:MostChapters", "0")]
    [InlineData("Encodings:Chapters:MostChapters", "forty")]
    public void AChapterSettingThatCannotBeReadIsRefusedNamingTheSetting(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, refusal.Message, StringComparison.Ordinal);
        Assert.True(new EncodingValidation().Validate(null, Written(key, value)).Failed);
    }

    [Fact]
    public void AToleranceThatWouldAdmitEveryPairIsRefusedAgainstTheGridItWasWrittenBeside()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(14),
            Read(
                ("Encodings:Chapters:Grid", "00:00:30"),
                ("Encodings:Chapters:GridTolerance", "00:00:14")).Chapters.GridTolerance);

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(
            ("Encodings:Chapters:Grid", "00:00:30"),
            ("Encodings:Chapters:GridTolerance", "00:00:15")));

        Assert.Contains("Encodings:Chapters:GridTolerance", refusal.Message, StringComparison.Ordinal);
    }

    private static EncodingOptions Written(string key, string value)
    {
        var options = new EncodingOptions();
        options.ReadFrom(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(key, value)])
            .Build());

        return options;
    }

    private static EncodeSettings Read(params (string Key, string Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        var options = new EncodingOptions();
        options.ReadFrom(configuration);

        return options.Read();
    }
}
