using Carina.Contracts;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeSearchTests
{
    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeService Channel = new(4, 1024);

    [Fact]
    public void AKeywordOfOneLetterWouldWalkTheWholeTableAndIsRefused()
        => Assert.Null(ProgrammeSearch.For("あ", null, null));

    [Fact]
    public void ASearchNobodyNarrowedWouldHandBackTheWholeTableAndIsRefused()
        => Assert.Null(ProgrammeSearch.For(null, null, null));

    [Fact]
    public void HowTheAnswerIsSortedAndPagedIsNotACondition()
        => Assert.Null(ProgrammeSearch.For(
            null,
            null,
            null,
            ProgrammeSort.Name,
            true,
            page: 3,
            perPage: 100));

    [Fact]
    public void WhereToLookNarrowsNothingWithoutAWordToLookForAndIsRefusedOnItsOwn()
        => Assert.Null(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Fields = [ProgrammeField.Title] }));

    [Fact]
    public void ConditionsAskedForAsNothingAtAllAreTheSameAsNotAskingForThem()
    {
        Assert.Null(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Genres = [], Channels = [], Fields = [] }));
        Assert.Null(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Exclude = "   " }));
        Assert.Null(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { System = TuneSystem.Unspecified }));
    }

    [Fact]
    public void AnExcludedWordOnItsOwnIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Exclude = "再放送" }));

    [Fact]
    public void AGenreOnItsOwnIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Genres = [8] }));

    [Fact]
    public void ABroadcastTypeOnItsOwnIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { System = TuneSystem.IsdbT }));

    [Fact]
    public void AChannelOnItsOwnIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Channels = [Channel] }));

    [Fact]
    public void EitherEndOfASpanOnItsOwnIsEnoughToAskWith()
    {
        Assert.NotNull(ProgrammeSearch.For(null, From, null));
        Assert.NotNull(ProgrammeSearch.For(null, null, From));
    }

    [Fact]
    public void AConditionBesideTheKeywordDoesNotBuyAKeywordOfOneLetterItsWayIn()
        => Assert.Null(ProgrammeSearch.For(
            "あ",
            null,
            null,
            conditions: new ProgrammeConditions { Genres = [8] }));

    [Fact]
    public void AKeywordNobodyNamedLeavesNoWordToLookFor()
    {
        ProgrammeSearch asked = ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Genres = [8] })!;

        Assert.Equal(string.Empty, asked.Keyword);
        Assert.Empty(asked.Words);
    }

    [Fact]
    public void TwoLettersIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For("news", null, null));

    [Fact]
    public void SurroundingSpaceDoesNotCountTowardsTheKeyword()
        => Assert.Null(ProgrammeSearch.For("  a  ", null, null));

    [Fact]
    public void AKeywordIsKeptWithoutItsSurroundingSpace()
        => Assert.Equal("news", ProgrammeSearch.For("  news  ", null, null)!.Keyword);

    [Fact]
    public void APageSizeNobodyNamedFallsBackRatherThanFetchingEverything()
        => Assert.Equal(ProgrammeSearch.DefaultPerPage, ProgrammeSearch.For("news", null, null)!.PerPage);

    [Fact]
    public void APageSizeBeyondTheCeilingIsBroughtDownToIt()
        => Assert.Equal(
            ProgrammeSearch.MostPerPage,
            ProgrammeSearch.For("news", null, null, perPage: 5_000)!.PerPage);

    [Fact]
    public void APageSizeOfNothingIsNotHonoured()
        => Assert.Equal(ProgrammeSearch.DefaultPerPage, ProgrammeSearch.For("news", null, null, perPage: 0)!.PerPage);

    [Fact]
    public void PagesCountFromOneHoweverTheyAreAskedFor()
        => Assert.Equal(1, ProgrammeSearch.For("news", null, null, page: -3)!.Page);

    [Fact]
    public void ASpanLongerThanAMonthIsRefused()
        => Assert.Null(ProgrammeSearch.For("news", From, From.AddDays(32)));

    [Fact]
    public void AMonthIsAllowed()
        => Assert.NotNull(ProgrammeSearch.For("news", From, From + ProgrammeSearch.LongestSpan));

    [Fact]
    public void ASpanThatRunsBackwardsIsRefused()
        => Assert.Null(ProgrammeSearch.For("news", From, From.AddHours(-1)));

    [Fact]
    public void AMomentWithoutAnOffsetIsRefusedRatherThanGuessedAt()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Unspecified),
            null));

    [Fact]
    public void OneSidedSpansAreAllowedBecauseTheKeywordAlreadyNarrows()
    {
        Assert.NotNull(ProgrammeSearch.For("news", From, null));
        Assert.NotNull(ProgrammeSearch.For("news", null, From));
    }

    [Fact]
    public void ASpacedKeywordIsAskedForAsEveryWordAtOnce()
        => Assert.Equal(["夏", "絶景"], Asking("夏　絶景").Words);

    [Fact]
    public void WordsAreLoweredOnceSoTheStoreDoesNotHaveTo()
        => Assert.Equal(["news"], Asking("NEWS").Words);

    [Fact]
    public void AKeywordOfNothingButOneLetterWordsWouldWalkTheWholeTableAndIsRefused()
        => Assert.Null(ProgrammeSearch.For("夏 空", null, null));

    [Fact]
    public void AOneLetterWordRidesAlongWithAWordThatCanNarrow()
        => Assert.Equal(["news", "7"], Asking("news 7").Words);

    [Fact]
    public void MoreWordsThanTheCeilingIsRefusedRatherThanRunAsManyScans()
        => Assert.Null(ProgrammeSearch.For(
            string.Join(' ', Enumerable.Repeat("ab", ProgrammeSearch.MostWords + 1)),
            null,
            null));

    [Fact]
    public void TheCeilingItselfIsAllowed()
        => Assert.NotNull(ProgrammeSearch.For(
            string.Join(' ', Enumerable.Repeat("ab", ProgrammeSearch.MostWords)),
            null,
            null));

    [Fact]
    public void NoFieldNamedMeansBothOfThem()
        => Assert.Equal([ProgrammeField.Title, ProgrammeField.Description], Asking("news").Fields);

    [Fact]
    public void AnEmptyListOfFieldsIsTheSameAsNamingNone()
        => Assert.Equal(
            [ProgrammeField.Title, ProgrammeField.Description],
            Asking("news", new ProgrammeConditions { Fields = [] }).Fields);

    [Fact]
    public void OneFieldNamedIsTheOnlyOneLookedIn()
        => Assert.Equal(
            [ProgrammeField.Title],
            Asking("news", new ProgrammeConditions { Fields = [ProgrammeField.Title] }).Fields);

    [Fact]
    public void TheSameFieldTwiceIsStillOneField()
        => Assert.Equal(
            [ProgrammeField.Title],
            Asking("news", new ProgrammeConditions
            {
                Fields = [ProgrammeField.Title, ProgrammeField.Title],
            }).Fields);

    [Fact]
    public void AFieldNobodyDefinedIsRefusedRatherThanPassedToTheStore()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions { Fields = [(ProgrammeField)9] }));

    [Fact]
    public void ASortNobodyDefinedIsRefusedRatherThanPassedToTheStore()
        => Assert.Null(ProgrammeSearch.For("news", null, null, (ProgrammeSort)7));

    [Fact]
    public void NothingExcludedIsTheOrdinaryCase()
        => Assert.Empty(Asking("news").ExcludedWords);

    [Fact]
    public void BlankIsTheSameAsExcludingNothing()
        => Assert.Empty(Asking("news", new ProgrammeConditions { Exclude = "   " }).ExcludedWords);

    [Fact]
    public void EachExcludedWordIsAskedForSeparately()
        => Assert.Equal(
            ["再放送", "ダイジェスト"],
            Asking("news", new ProgrammeConditions { Exclude = "再放送 ダイジェスト" }).ExcludedWords);

    [Fact]
    public void AnExcludedWordOfOneLetterWouldTakeOutTooMuchAndIsRefused()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions { Exclude = "再" }));

    [Fact]
    public void NoGenreNamedLeavesEveryGenreIn()
        => Assert.Empty(Asking("news").Genres);

    [Fact]
    public void GenresAreKeptInTheOrderTheyWereAskedForWithoutRepeats()
        => Assert.Equal(
            [8, 6],
            Asking("news", new ProgrammeConditions { Genres = [8, 6, 8] }).Genres);

    [Fact]
    public void AGenreOutsideTheFourBitsTheStandardGivesItIsRefused()
    {
        Assert.Null(ProgrammeSearch.For("news", null, null, conditions: new ProgrammeConditions { Genres = [16] }));
        Assert.Null(ProgrammeSearch.For("news", null, null, conditions: new ProgrammeConditions { Genres = [-1] }));
    }

    [Fact]
    public void EveryGenreThereIsCanBeAskedForAtOnce()
        => Assert.NotNull(ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions { Genres = [.. Enumerable.Range(0, 16)] }));

    [Fact]
    public void ABroadcastTypeNobodyNamedIsTheSameAsNotAskingForOne()
    {
        Assert.Null(Asking("news").System);
        Assert.Null(Asking("news", new ProgrammeConditions { System = TuneSystem.Unspecified }).System);
    }

    [Fact]
    public void ABroadcastTypeNobodyDefinedIsRefused()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions { System = (TuneSystem)9 }));

    [Fact]
    public void ChannelsAreKeptWithoutRepeats()
        => Assert.Equal(
            [new ProgrammeService(4, 1024), new ProgrammeService(4, 1032)],
            Asking("news", new ProgrammeConditions
            {
                Channels = [new ProgrammeService(4, 1024), new ProgrammeService(4, 1032), new ProgrammeService(4, 1024)],
            }).Channels);

    [Fact]
    public void MoreChannelsThanTheCeilingIsRefused()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions
            {
                Channels =
                [
                    .. Enumerable
                        .Range(0, ProgrammeSearch.MostChannels + 1)
                        .Select(carried => new ProgrammeService(4, carried)),
                ],
            }));

    [Fact]
    public void UntilABroadcastTypeIsResolvedTheSearchCoversEveryService()
        => Assert.Null(Asking("news").Services);

    [Fact]
    public void ResolvingABroadcastTypeLeavesEveryOtherConditionAlone()
    {
        ProgrammeSearch narrowed = Asking(
            "news",
            new ProgrammeConditions { Exclude = "再放送", Genres = [8] })
            .Over([new ProgrammeService(4, 1024)]);

        Assert.Equal([new ProgrammeService(4, 1024)], narrowed.Services);
        Assert.Equal(["再放送"], narrowed.ExcludedWords);
        Assert.Equal([8], narrowed.Genres);
        Assert.Equal("news", narrowed.Keyword);
    }

    [Fact]
    public void ABroadcastTypeThatCarriesNoServiceNarrowsToNothingRatherThanToEverything()
        => Assert.Empty(Asking("news").Over([]).Services!);

    private static ProgrammeSearch Asking(string keyword, ProgrammeConditions? conditions = null)
        => ProgrammeSearch.For(keyword, null, null, conditions: conditions)!;
}
