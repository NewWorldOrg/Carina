using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingQueryTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AQueryThatNamesNothingReadsTheFirstPageOfEverything()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(null, null));

        Assert.Equal(string.Empty, query.Keyword.Asked);
        Assert.Empty(query.Keyword.Words);
        Assert.Null(query.Standing);
        Assert.Empty(query.Outcomes);
        Assert.Null(query.Drops);
        Assert.Empty(query.Channels);
        Assert.Equal(1, query.Page);
        Assert.Equal(RecordingQuery.DefaultPerPage, query.PerPage);
        Assert.Equal(RecordingSort.StartedAt, query.Sort);
        Assert.False(query.Descending);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(RecordingQuery.MostPerPage - 1, RecordingQuery.MostPerPage - 1)]
    [InlineData(RecordingQuery.MostPerPage, RecordingQuery.MostPerPage)]
    [InlineData(null, RecordingQuery.DefaultPerPage)]
    public void APageSizeWithinTheCeilingIsTheOneThatWasAskedFor(int? asked, int carried)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, perPage: asked));

        Assert.Equal(carried, query.PerPage);
    }

    [Theory]
    [InlineData(RecordingQuery.MostPerPage + 1)]
    [InlineData(int.MaxValue)]
    public void APageSizeAboveTheCeilingIsCutDownToItRatherThanRefused(int asked)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, perPage: asked));

        Assert.Equal(RecordingQuery.MostPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOneIsReadAsTheSizeNobodyAskedForAnythingElseThan(int asked)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, perPage: asked));

        Assert.Equal(RecordingQuery.DefaultPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageNumberBelowTheFirstIsRefusedRatherThanReadAsTheFirst(int asked)
        => Assert.Null(RecordingQuery.For(null, null, page: asked));

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void APageNumberAtOrAboveTheFirstIsTheOneThatWasAskedFor(int? asked, int carried)
        => Assert.Equal(carried, Assert.IsType<RecordingQuery>(RecordingQuery.For(null, null, page: asked)).Page);

    [Fact]
    public void ASpanExactlyAsLongAsTheCeilingIsRead()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(Noon, Noon + RecordingQuery.LongestSpan));

        Assert.Equal(Noon, query.From);
        Assert.Equal(Noon + RecordingQuery.LongestSpan, query.To);
    }

    [Fact]
    public void ASpanOneTickLongerThanTheCeilingIsRefused()
        => Assert.Null(RecordingQuery.For(
            Noon,
            Noon + RecordingQuery.LongestSpan + TimeSpan.FromTicks(1)));

    [Fact]
    public void ASpanThatEndsWhereItBeganNarrowsToNothingAndIsRefused()
        => Assert.Null(RecordingQuery.For(Noon, Noon));

    [Fact]
    public void ASpanThatRunsBackwardsIsRefused()
        => Assert.Null(RecordingQuery.For(Noon, Noon.AddSeconds(-1)));

    [Fact]
    public void OneEndOfASpanOnItsOwnIsRead()
    {
        Assert.NotNull(RecordingQuery.For(Noon, null));
        Assert.NotNull(RecordingQuery.For(null, Noon));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ATimeThatIsNotReadAgainstTheSameClockIsRefused(DateTimeKind kind)
    {
        var moment = new DateTime(2026, 8, 24, 12, 0, 0, kind);

        Assert.Null(RecordingQuery.For(moment, null));
        Assert.Null(RecordingQuery.For(null, moment));
    }

    [Fact]
    public void ASortNobodyNamedIsRefusedRatherThanFallingBackToTheDefault()
        => Assert.Null(RecordingQuery.For(null, null, (RecordingSort)99));

    [Fact]
    public void AStateNobodyNamedIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Standing = (RecordingStanding)99 }));

    [Fact]
    public void ADropReadingNobodyNamedIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Drops = (DropReading)99 }));

    [Fact]
    public void AnOutcomeNobodyNamedIsRefusedEvenBesideOnesThatWereNamed()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Outcomes = [RecordingOutcome.Complete, (RecordingOutcome)99],
            }));

    [Fact]
    public void TheSameOutcomeAskedForTwiceIsCarriedOnce()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Outcomes = [RecordingOutcome.Failed, RecordingOutcome.Failed],
            }));

        Assert.Equal([RecordingOutcome.Failed], query.Outcomes);
    }

    [Fact]
    public void ARecordingStillBeingWrittenHasNoOutcomeYetSoAskingForBothIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Standing = RecordingStanding.InFlight,
                Outcomes = [RecordingOutcome.Complete],
            }));

    [Fact]
    public void AskingForEndedRecordingsOfANamedOutcomeIsRead()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Standing = RecordingStanding.Ended,
                Outcomes = [RecordingOutcome.Truncated],
            }));

        Assert.Equal(RecordingStanding.Ended, query.Standing);
        Assert.Equal([RecordingOutcome.Truncated], query.Outcomes);
    }

    [Fact]
    public void AskingOnlyForRecordingsStillBeingWrittenIsRead()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Standing = RecordingStanding.InFlight }));

        Assert.Equal(RecordingStanding.InFlight, query.Standing);
        Assert.Empty(query.Outcomes);
    }

    [Fact]
    public void AsManyChannelsAsTheCeilingAllowsAreRead()
    {
        ProgrammeService[] channels = [.. Enumerable
            .Range(1, RecordingQuery.MostChannels)
            .Select(service => new ProgrammeService(4, service))];

        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Channels = channels }));

        Assert.Equal(RecordingQuery.MostChannels, query.Channels.Count);
    }

    [Fact]
    public void OneChannelOverTheCeilingIsRefused()
    {
        ProgrammeService[] channels = [.. Enumerable
            .Range(1, RecordingQuery.MostChannels + 1)
            .Select(service => new ProgrammeService(4, service))];

        Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Channels = channels }));
    }

    [Fact]
    public void TheSameChannelAskedForTwiceCountsOnceAgainstTheCeiling()
    {
        ProgrammeService[] channels = [.. Enumerable
            .Range(1, RecordingQuery.MostChannels + 1)
            .Select(service => new ProgrammeService(4, 1))];

        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Channels = channels }));

        Assert.Single(query.Channels);
    }

    [Fact]
    public void EveryDropReadingIsOneTheQueryCarries()
    {
        foreach (DropReading reading in Enum.GetValues<DropReading>())
        {
            RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
                null,
                null,
                conditions: new RecordingConditions { Drops = reading }));

            Assert.Equal(reading, query.Drops);
        }

        Assert.Equal(3, Enum.GetValues<DropReading>().Length);
    }

    [Fact]
    public void AKeywordOfASingleLetterIsReadRatherThanHeldToTheTwoTheGuideAsksFor()
    {
        RecordingQuery query = Keyworded("a");

        Assert.Equal("a", query.Keyword.Asked);
        Assert.Equal(["a"], query.Keyword.Words);
    }

    [Fact]
    public void AKeywordAsLongAsTheCeilingAllowsIsRead()
        => Assert.Equal(
            RecordingKeyword.LongestKeyword,
            Keyworded(new string('a', RecordingKeyword.LongestKeyword)).Keyword.Asked.Length);

    [Fact]
    public void OneLetterOverTheCeilingIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Keyword = new string('a', RecordingKeyword.LongestKeyword + 1) }));

    [Fact]
    public void AsManyWordsAsTheCeilingAllowsAreRead()
        => Assert.Equal(
            RecordingKeyword.MostWords,
            Keyworded(Words(RecordingKeyword.MostWords)).Keyword.Words.Count);

    [Fact]
    public void OneWordOverTheCeilingIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Keyword = Words(RecordingKeyword.MostWords + 1) }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("　")]
    [InlineData("　 　")]
    public void AKeywordOfNothingButSpacesAsksForNothingRatherThanBeingRefused(string? asked)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Keyword = asked }));

        Assert.Empty(query.Keyword.Words);
    }

    [Fact]
    public void TheWordsAreFoldedTheWayTheStoreFoldsTheTextItSearches()
        => Assert.Equal(["needy", "girl"], Keyworded("ＮＥＥＤＹ　ＧＩＲＬ").Keyword.Words);

    [Fact]
    public void AKeywordWrittenAroundAFullWidthSpaceIsReadAsTwoWords()
        => Assert.Equal(2, Keyworded("weather　today").Keyword.Words.Count);

    [Fact]
    public void ADescendingSortOnTheProgrammeIsCarriedAsAsked()
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, RecordingSort.ProgrammeStartsAt, descending: true));

        Assert.Equal(RecordingSort.ProgrammeStartsAt, query.Sort);
        Assert.True(query.Descending);
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Range(1, count).Select(word => $"w{word}"));

    private static RecordingQuery Keyworded(string keyword)
        => Assert.IsType<RecordingQuery>(RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions { Keyword = keyword }));

    [Fact]
    public void TheBoundsTheseSurfacesHoldAreTheNumbersTheNormNames()
    {
        Assert.Equal(200, RecordingQuery.MostPerPage);
        Assert.Equal(50, RecordingQuery.DefaultPerPage);
        Assert.Equal(64, RecordingQuery.MostChannels);
        Assert.Equal(366, RecordingQuery.LongestSpan.TotalDays);
        Assert.Equal(500, RecordingStopReason.MaxLength);
    }

    [Fact]
    public void APageSizeOfTwoHundredIsTakenAndTwoHundredAndOneIsCutDownToIt()
    {
        Assert.Equal(200, Asked(200));
        Assert.Equal(200, Asked(201));
        Assert.Equal(199, Asked(199));
    }

    [Fact]
    public void AReasonOfFiveHundredLettersIsTakenAndFiveHundredAndOneIsNot()
    {
        Assert.Equal(500, new RecordingStopReason(new string('a', 500)).Value.Length);
        Assert.Null(RecordingStopReason.Read(new string('a', 501)));
    }

    [Fact]
    public void ASpanOfThreeHundredAndSixtySixDaysIsTakenAndOneDayMoreIsNot()
    {
        var began = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.NotNull(RecordingQuery.For(began, began.AddDays(366)));
        Assert.Null(RecordingQuery.For(began, began.AddDays(367)));
    }

    [Fact]
    public void SixtyFourChannelsAreTakenAndSixtyFiveAreNot()
    {
        Assert.NotNull(Over(64));
        Assert.Null(Over(65));
    }

    private static int Asked(int perPage)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, perPage: perPage));

        return query.PerPage;
    }

    private static RecordingQuery? Over(int channels)
        => RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Channels =
                [
                    .. Enumerable
                        .Range(1, channels)
                        .Select(service => new Carina.Domain.Programmes.ProgrammeService(4, service)),
                ],
            });
}
