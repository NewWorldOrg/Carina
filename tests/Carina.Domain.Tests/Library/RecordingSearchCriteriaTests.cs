using Carina.Domain.Library;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Library;

public sealed class RecordingSearchCriteriaTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriteriaThatNarrowNothingAreTheWholeLibraryNewestFirstRatherThanNothingAtAll()
    {
        RecordingSearchCriteria criteria = Read(RecordingSearchCriteria.For(null, null, null));

        Assert.Empty(criteria.Words);
        Assert.Empty(criteria.Channels);
        Assert.Empty(criteria.Outcomes);
        Assert.Null(criteria.Genre);
        Assert.Null(criteria.Quality);
        Assert.Null(criteria.From);
        Assert.Null(criteria.To);
        Assert.Null(criteria.After);
        Assert.Equal(RecordingSortKey.NewestFirst, criteria.Sort);
        Assert.Equal(RecordingSearchCriteria.DefaultPerPage, criteria.PerPage);
    }

    [Theory]
    [InlineData("鷹")]
    [InlineData("猫")]
    public void AKeywordOfOneLetterIsTheSearchSomebodyMeantRatherThanTooShortToRun(string asked)
        => Assert.Equal([asked], Read(RecordingSearchCriteria.For(asked, null, null)).Words);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　")]
    [InlineData("　　 ")]
    public void AKeywordThatIsNothingButSpaceIsNoKeywordAtAllRatherThanARefusal(string? asked)
    {
        RecordingSearchCriteria criteria = Read(RecordingSearchCriteria.For(asked, null, null));

        Assert.Empty(criteria.Words);
        Assert.Empty(criteria.Keyword);
    }

    [Fact]
    public void WordsAreTakenApartOnTheWideSpaceAsWellAsTheNarrowOne()
        => Assert.Equal(
            ["ニュース", "気象", "特集"],
            Read(RecordingSearchCriteria.For("ニュース　気象 特集", null, null)).Words);

    [Fact]
    public void AKeywordIsFoldedTheSameWayTheStoredTextIsSoACopiedTitleStillFinds()
        => Assert.Equal(
            [ProgrammeSearchText.Folded("ＮＥＥＤＹ"), ProgrammeSearchText.Folded("ｷﾞｮｳｻﾞ")],
            Read(RecordingSearchCriteria.For("ＮＥＥＤＹ ｷﾞｮｳｻﾞ", null, null)).Words);

    [Fact]
    public void AWordThatFoldsAwayToSpaceLeavesTheOtherWordsStanding()
        => Assert.Equal(["ニュース"], Read(RecordingSearchCriteria.For("\u00a0 ニュース", null, null)).Words);

    [Fact]
    public void AKeywordAsLongAsTheCeilingIsRead()
        => Assert.Equal(
            [new string('a', RecordingSearchCriteria.LongestKeyword)],
            Read(RecordingSearchCriteria.For(new string('a', RecordingSearchCriteria.LongestKeyword), null, null))
                .Words);

    [Fact]
    public void AKeywordPastTheCeilingIsRefusedRatherThanCutDown()
        => Assert.Null(
            RecordingSearchCriteria.For(new string('a', RecordingSearchCriteria.LongestKeyword + 1), null, null));

    [Fact]
    public void AsManyWordsAsTheCeilingAllowsAreRead()
        => Assert.Equal(
            RecordingSearchCriteria.MostWords,
            Read(RecordingSearchCriteria.For(Words(RecordingSearchCriteria.MostWords), null, null)).Words.Count);

    [Fact]
    public void MoreWordsThanTheCeilingAllowsAreRefused()
        => Assert.Null(RecordingSearchCriteria.For(Words(RecordingSearchCriteria.MostWords + 1), null, null));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(RecordingSearchCriteria.MostPerPage, RecordingSearchCriteria.MostPerPage)]
    [InlineData(9999, RecordingSearchCriteria.MostPerPage)]
    [InlineData(null, RecordingSearchCriteria.DefaultPerPage)]
    [InlineData(0, RecordingSearchCriteria.DefaultPerPage)]
    [InlineData(-1, RecordingSearchCriteria.DefaultPerPage)]
    public void APageSizeIsCutDownToTheCeilingRatherThanRefused(int? asked, int carried)
        => Assert.Equal(carried, Read(RecordingSearchCriteria.For(null, null, null, perPage: asked)).PerPage);

    [Fact]
    public void ASortKeyOutsideTheAllowedSetIsRefusedRatherThanReadAsTheDefault()
        => Assert.Null(RecordingSearchCriteria.For(null, null, null, sort: (RecordingSortKey)77));

    [Fact]
    public void NoSpanAtAllIsTheWholeLibraryRatherThanTheLatestFewDays()
    {
        RecordingSearchCriteria criteria = Read(RecordingSearchCriteria.For(null, null, null));

        Assert.Null(criteria.From);
        Assert.Null(criteria.To);
    }

    [Fact]
    public void ASpanExactlyAsLongAsTheCeilingIsRead()
    {
        RecordingSearchCriteria criteria = Read(
            RecordingSearchCriteria.For(null, Noon, Noon + RecordingSearchCriteria.LongestSpan));

        Assert.Equal(Noon, criteria.From);
        Assert.Equal(Noon + RecordingSearchCriteria.LongestSpan, criteria.To);
    }

    [Fact]
    public void ASpanPastTheCeilingIsRefusedOnlyBecauseSomebodyNamedBothEnds()
        => Assert.Null(
            RecordingSearchCriteria.For(
                null,
                Noon,
                Noon + RecordingSearchCriteria.LongestSpan + TimeSpan.FromDays(1)));

    [Fact]
    public void ASpanThatEndsBeforeItBeginsIsRefused()
        => Assert.Null(RecordingSearchCriteria.For(null, Noon, Noon.AddDays(-1)));

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ASpanKeptInAnythingButUtcIsRefused(DateTimeKind kind)
    {
        DateTime carried = new(2026, 8, 24, 12, 0, 0, kind);

        Assert.Null(RecordingSearchCriteria.For(null, carried, null));
        Assert.Null(RecordingSearchCriteria.For(null, null, carried));
    }

    [Fact]
    public void OnlyOneEndOfTheSpanIsEnoughToNameIt()
    {
        Assert.Equal(Noon, Read(RecordingSearchCriteria.For(null, Noon, null)).From);
        Assert.Equal(Noon, Read(RecordingSearchCriteria.For(null, null, Noon)).To);
    }

    [Fact]
    public void TheFourQualityLevelsAreTheOnlyOnesTheLibraryTakes()
    {
        foreach (QualityLevel level in Enum.GetValues<QualityLevel>())
        {
            Assert.Equal(
                level,
                Read(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
                {
                    Quality = level,
                })).Quality);
        }
    }

    [Fact]
    public void AQualityLevelOutsideThoseFourIsRefused()
        => Assert.Null(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
        {
            Quality = (QualityLevel)9,
        }));

    [Fact]
    public void TheOutcomesAskedForAreKeptWithoutRepeatingOne()
        => Assert.Equal(
            [RecordingOutcome.Truncated, RecordingOutcome.Failed],
            Read(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
            {
                Outcomes = [RecordingOutcome.Truncated, RecordingOutcome.Failed, RecordingOutcome.Truncated],
            })).Outcomes);

    [Fact]
    public void AnOutcomeOutsideTheThreeARecordingCanEndInIsRefused()
        => Assert.Null(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
        {
            Outcomes = [(RecordingOutcome)8],
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(RecordingSearchCriteria.HighestGenre)]
    public void AGenreWithinTheBroadcastVocabularyIsRead(int asked)
        => Assert.Equal(
            asked,
            Read(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
            {
                Genre = asked,
            })).Genre);

    [Theory]
    [InlineData(-1)]
    [InlineData(RecordingSearchCriteria.HighestGenre + 1)]
    public void AGenreOutsideThatVocabularyIsRefused(int asked)
        => Assert.Null(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
        {
            Genre = asked,
        }));

    [Fact]
    public void TheChannelsAskedForAreKeptWithoutRepeatingOne()
        => Assert.Equal(
            [new ProgrammeService(1, 1024), new ProgrammeService(1, 1032)],
            Read(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
            {
                Channels = [new ProgrammeService(1, 1024), new ProgrammeService(1, 1032), new ProgrammeService(1, 1024)],
            })).Channels);

    [Fact]
    public void MoreChannelsThanTheCeilingAllowsAreRefused()
        => Assert.Null(RecordingSearchCriteria.For(null, null, null, conditions: new RecordingSearchConditions
        {
            Channels = [.. Enumerable
                .Range(0, RecordingSearchCriteria.MostChannels + 1)
                .Select(carried => new ProgrammeService(1, 1024 + carried))],
        }));

    [Fact]
    public void ACursorNamesWhereTheLastPageStoppedSoTheNextOneCarriesOn()
    {
        RecordingCursor after = new(Noon, new RecordingId(Guid.NewGuid()));

        Assert.Equal(after, Read(RecordingSearchCriteria.For(null, null, null, after: after)).After);
    }

    [Fact]
    public void ACursorKeptInAnythingButUtcIsRefused()
        => Assert.Null(RecordingSearchCriteria.For(
            null,
            null,
            null,
            after: new RecordingCursor(
                new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Unspecified),
                new RecordingId(Guid.NewGuid()))));

    private static string Words(int count)
        => string.Join(' ', Enumerable.Range(0, count).Select(carried => $"word{carried}"));

    private static RecordingSearchCriteria Read(RecordingSearchCriteria? criteria)
        => Assert.IsType<RecordingSearchCriteria>(criteria);
}
