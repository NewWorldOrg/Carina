using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SeriesNamesTests
{
    private static readonly string New = char.ConvertFromUtf32(0x1F21F);

    private static readonly string Captioned = char.ConvertFromUtf32(0x1F211);

    private static readonly string Final = char.ConvertFromUtf32(0x1F221);

    [Theory(DisplayName = "an episode is read in each of the forms a programme name writes it")]
    [InlineData("架空の物語 #3", 3)]
    [InlineData("架空の物語 #03", 3)]
    [InlineData("架空の物語 ＃１２", 12)]
    [InlineData("架空の物語 第4話", 4)]
    [InlineData("架空の物語 第５回", 5)]
    [InlineData("架空の物語 6話", 6)]
    [InlineData("架空の物語 第一話", 1)]
    [InlineData("架空の物語 第十話", 10)]
    [InlineData("架空の物語 第十二話", 12)]
    [InlineData("架空の物語 第二十話", 20)]
    [InlineData("架空の物語 第九十九話", 99)]
    public void AnEpisodeIsReadInEachForm(string name, int episode)
    {
        Assert.Equal(episode, SeriesNames.EpisodeOf(name));
        Assert.Equal("架空の物語", SeriesNames.Of(name));
    }

    [Theory(DisplayName = "a season, part or chapter number is not an episode, and stays in the series name")]
    [InlineData("架空の物語 第2期")]
    [InlineData("架空の物語 第3シーズン")]
    [InlineData("架空の物語 第2章")]
    [InlineData("架空の物語 第2部")]
    public void ASeasonNumberIsNotAnEpisode(string name)
    {
        Assert.Null(SeriesNames.EpisodeOf(name));
        Assert.Equal(name, SeriesNames.Of(name));
    }

    [Theory(DisplayName = "a number that is part of the name is not an episode")]
    [InlineData("架空の旅2001年")]
    [InlineData("架空の学園3年B組")]
    public void ANumberInsideTheNameIsNotAnEpisode(string name)
    {
        Assert.Null(SeriesNames.EpisodeOf(name));
        Assert.Equal(name, SeriesNames.Of(name));
    }

    [Fact(DisplayName = "a season number stays while the episode after it is taken out")]
    public void ASeasonNumberStaysWhileTheEpisodeGoes()
    {
        Assert.Equal("架空の物語 第2期", SeriesNames.Of("架空の物語 第2期 #5"));
        Assert.Equal(5, SeriesNames.EpisodeOf("架空の物語 第2期 #5"));
    }

    [Fact(DisplayName = "the marks are taken out, whether written as symbols or in brackets")]
    public void TheMarksAreTakenOut()
    {
        Assert.Equal("架空の物語", SeriesNames.Of($"{New}架空の物語 #1{Captioned}"));
        Assert.Equal("架空の物語", SeriesNames.Of("[新]架空の物語[字] #1"));
        Assert.Equal("架空の物語", SeriesNames.Of("［終］架空の物語 #12"));
        Assert.Equal("架空の物語", SeriesNames.Of($"架空の物語 #12{Final}"));
    }

    [Fact(DisplayName = "the subtitle in brackets after an episode goes with it")]
    public void TheSubtitleAfterAnEpisodeGoes()
    {
        Assert.Equal("架空の物語", SeriesNames.Of("架空の物語 #3「架空の副題」"));
        Assert.Equal("架空の物語", SeriesNames.Of("架空の物語 第3話 「架空の副題」"));
    }

    [Theory(DisplayName = "BR-CD-011: a subtitle that ends the name goes without an episode, in 「」 or 『』 or after ▽, half-width brackets folded")]
    [InlineData("アニメ 架空の忍者「架空の段」", "アニメ 架空の忍者")]
    [InlineData("アニメ 架空の忍者『架空の段』", "アニメ 架空の忍者")]
    [InlineData("アニメ 架空の忍者 ｢架空の段｣", "アニメ 架空の忍者")]
    [InlineData("アニメ 架空の忍者「架空の段」「後編」", "アニメ 架空の忍者")]
    [InlineData("アニメ 架空の忍者「架空の『巻』の段」", "アニメ 架空の忍者")]
    [InlineData("架空のニュース ▽架空の話題▽別の「架空の」話題", "架空のニュース")]
    [InlineData("架空の物語「架空の題」", "架空の物語")]
    public void ASubtitleThatEndsTheNameGoes(string name, string series)
    {
        Assert.Equal(series, SeriesNames.Of(name));
        Assert.Null(SeriesNames.EpisodeOf(name));
    }

    [Fact(DisplayName = "BR-CD-011: two days of a daily programme whose names differ only in the subtitle and the marks are one series")]
    public void TwoDaysDifferingOnlyInTheSubtitleAreOneSeries()
    {
        string? monday = SeriesNames.Of($"アニメ 架空の忍者「架空の巻物の段」{Captioned}");
        string? tuesday = SeriesNames.Of("[新]アニメ 架空の忍者「架空の手裏剣の段」[字]");

        Assert.Equal("アニメ 架空の忍者", monday);
        Assert.True(SeriesNames.Same(monday, tuesday));
        Assert.True(SeriesNames.Link(monday, tuesday));
    }

    [Theory(DisplayName = "BR-CD-011: a name in brackets is the programme's own, alone or after a word of fewer than four characters, and stays while a subtitle after it goes")]
    [InlineData("「架空の物語」", "「架空の物語」")]
    [InlineData("[新]「架空の物語」 #1", "「架空の物語」")]
    [InlineData("「架空の物語」「架空の副題」", "「架空の物語」")]
    [InlineData("アニメ「架空の物語」", "アニメ「架空の物語」")]
    [InlineData("▽架空の特集", "▽架空の特集")]
    public void ANameInBracketsStays(string name, string series)
    {
        Assert.Equal(series, SeriesNames.Of(name));
    }

    [Theory(DisplayName = "a series name read again is the same, and one kept by the rule before subtitles went is made as it is now")]
    [InlineData("アニメ 架空の忍者「架空の段」", "アニメ 架空の忍者")]
    [InlineData("架空の物語 第2期", "架空の物語 第2期")]
    [InlineData("架空の物語", "架空の物語")]
    [InlineData("「架空の物語」", "「架空の物語」")]
    [InlineData("アニメ「架空の物語」", "アニメ「架空の物語」")]
    public void ASeriesNameReadAgainIsTheSame(string kept, string series)
    {
        Assert.Equal(series, SeriesNames.Of(kept));
        Assert.Equal(series, SeriesNames.Of(SeriesNames.Of(kept)!));
    }

    [Fact(DisplayName = "every episode in the name is taken out, and the first one is the episode")]
    public void EveryEpisodeIsTakenOut()
    {
        Assert.Equal("架空の物語", SeriesNames.Of("架空の物語 #7 #8"));
        Assert.Equal(7, SeriesNames.EpisodeOf("架空の物語 #7 #8"));
    }

    [Fact(DisplayName = "the name is cut at its first full-width space, after the marks and spaces at its ends")]
    public void TheNameIsCutAtTheFirstFullWidthSpace()
    {
        Assert.Equal("架空の物語", SeriesNames.Of("架空の物語　第3話「架空の副題」"));
        Assert.Equal("架空の物語", SeriesNames.Of($"{New}　架空の物語　架空の添え書き"));
        Assert.Equal("架空の物語", SeriesNames.Of("　架空の物語　"));
        Assert.Equal(3, SeriesNames.EpisodeOf("架空の物語　第3話「架空の副題」"));
    }

    [Fact(DisplayName = "the name is folded with NFKC")]
    public void TheNameIsFolded()
    {
        Assert.Equal("ABC物語", SeriesNames.Of("ＡＢＣ物語 #2"));
    }

    [Fact(DisplayName = "a name that is nothing but marks and an episode has no series name")]
    public void ANameOfNothingButMarksHasNoSeriesName()
    {
        Assert.Null(SeriesNames.Of($"{New} #1"));
        Assert.Null(SeriesNames.Of(string.Empty));
        Assert.Null(SeriesNames.EpisodeOf("架空の物語"));
    }

    [Fact(DisplayName = "an episode too large to count is not read")]
    public void AnEpisodeTooLargeIsNotRead()
    {
        Assert.Null(SeriesNames.EpisodeOf("架空の物語 #99999999999"));
    }
}
