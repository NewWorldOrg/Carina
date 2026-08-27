using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;

namespace Carina.Infrastructure.Tests;

public sealed class ProgrammeSearchQueryTests
{
    private static readonly Dictionary<string, string> ASampleValueForEveryWord = new(StringComparer.Ordinal)
    {
        [ProgrammeSearchQuery.Keyword] = "%E7%B5%B6%E6%99%AF",
        [ProgrammeSearchQuery.Exclude] = "%E5%86%8D%E6%94%BE%E9%80%81",
        [ProgrammeSearchQuery.Fields] = "title",
        [ProgrammeSearchQuery.Genre] = "8",
        [ProgrammeSearchQuery.Type] = "isdbT",
        [ProgrammeSearchQuery.Channel] = "4-1049",
        [ProgrammeSearchQuery.From] = "2026-08-18T00:00:00Z",
        [ProgrammeSearchQuery.To] = "2026-08-19T00:00:00Z",
        [ProgrammeSearchQuery.Sort] = "name",
        [ProgrammeSearchQuery.Descending] = "true",
        [ProgrammeSearchQuery.Page] = "3",
        [ProgrammeSearchQuery.PerPage] = "7",
    };

    public static TheoryData<string> EveryWord()
    {
        var carried = new TheoryData<string>();

        foreach (ProgrammeSearchTerm term in ProgrammeSearchQuery.Vocabulary)
        {
            carried.Add(term.Name);
        }

        return carried;
    }

    [Theory]
    [MemberData(nameof(EveryWord))]
    public void EveryWordTheVocabularyDeclaresChangesWhatIsAsked(string name)
    {
        string plain = name == ProgrammeSearchQuery.Exclude ? "keyword=news" : "exclude=zzz";

        Assert.NotEqual(
            Spelt(ProgrammeSearchQuery.Read(plain)),
            Spelt(ProgrammeSearchQuery.Read($"{plain}&{name}={ASampleValueForEveryWord[name]}")));
    }

    [Fact]
    public void TheSampleValuesCoverTheVocabularyAndNothingBeside()
    {
        Assert.Equal(
            [.. ProgrammeSearchQuery.Vocabulary.Select(term => term.Name).Order(StringComparer.Ordinal)],
            [.. ASampleValueForEveryWord.Keys.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void ANameTheVocabularyDoesNotCarryIsNotRead()
    {
        Assert.Equal(
            Spelt(ProgrammeSearchQuery.Read("keyword=news")),
            Spelt(ProgrammeSearchQuery.Read("keyword=news&title=news&q=news&keywords=news")));
    }

    [Fact]
    public void ASearchNobodyNarrowedIsRefused()
    {
        Assert.Null(ProgrammeSearchQuery.Read(null));
        Assert.Null(ProgrammeSearchQuery.Read(string.Empty));
        Assert.Null(ProgrammeSearchQuery.Read("?sort=name&descending=true&page=2&perPage=100"));
    }

    [Fact]
    public void TheLeadingQuestionMarkTheRequestCarriesIsNotPartOfTheFirstName()
    {
        Assert.Equal(["news"], ProgrammeSearchQuery.Read("?keyword=news")!.Words);
    }

    [Fact]
    public void APlusStandsForASpaceBetweenTwoWords()
    {
        Assert.Equal(["夏の", "絶景"], ProgrammeSearchQuery.Read("keyword=夏の+絶景")!.Words);
    }

    [Fact]
    public void WhatWasPercentEncodedComesBackAsItWasWritten()
    {
        Assert.Equal(["絶景"], ProgrammeSearchQuery.Read("keyword=%E7%B5%B6%E6%99%AF")!.Words);
    }

    [Fact]
    public void EveryWordOfTheVocabularyIsReadFromTheOneQuery()
    {
        string asked = string.Join(
            '&',
            ASampleValueForEveryWord.Select(word => $"{word.Key}={word.Value}"));
        ProgrammeSearch read = ProgrammeSearchQuery.Read(asked)!;

        Assert.Equal(["絶景"], read.Words);
        Assert.Equal(["再放送"], read.ExcludedWords);
        Assert.Equal([ProgrammeField.Title], read.Fields);
        Assert.Equal([8], read.Genres);
        Assert.Equal(TuneSystem.IsdbT, read.System);
        Assert.Equal([new ProgrammeService(4, 1049)], read.Channels);
        Assert.Equal(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc), read.From);
        Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc), read.To);
        Assert.Equal(ProgrammeSort.Name, read.Sort);
        Assert.True(read.Descending);
        Assert.Equal(3, read.Page);
        Assert.Equal(7, read.PerPage);
    }

    [Fact]
    public void ANameThatMayBeGivenMoreThanOnceGathersEveryValue()
    {
        ProgrammeSearch read = ProgrammeSearchQuery.Read(
            "keyword=news&genre=8&genre=6&channel=4-1049&channel=4-1032&fields=title&fields=description")!;

        Assert.Equal([8, 6], read.Genres);
        Assert.Equal([new ProgrammeService(4, 1049), new ProgrammeService(4, 1032)], read.Channels);
        Assert.Equal([ProgrammeField.Title, ProgrammeField.Description], read.Fields);
    }

    [Fact]
    public void ANameThatMayBeGivenOnceIsReadFromTheFirstOfThem()
    {
        Assert.Equal(["first"], ProgrammeSearchQuery.Read("keyword=first&keyword=second")!.Words);
    }

    [Theory]
    [InlineData("keyword=news&sort=name;DROP TABLE programme")]
    [InlineData("keyword=news&sort=7")]
    [InlineData("keyword=news&fields=summary;DROP TABLE programme")]
    [InlineData("keyword=news&genre=kind")]
    [InlineData("keyword=news&genre=99")]
    [InlineData("keyword=news&type=vhf")]
    [InlineData("keyword=news&channel=not-a-channel")]
    [InlineData("keyword=news&channel=4-99999")]
    [InlineData("keyword=news&from=yesterday")]
    [InlineData("keyword=news&to=tomorrow")]
    [InlineData("keyword=news&page=first")]
    [InlineData("keyword=news&perPage=all")]
    [InlineData("keyword=news&descending=yes")]
    [InlineData("keyword=あ")]
    [InlineData("exclude=再")]
    public void AValueTheSearchCannotHoldIsRefusedRatherThanPassedOn(string asked)
        => Assert.Null(ProgrammeSearchQuery.Read(asked));

    [Fact]
    public void ABroadcastTypeNobodyNamedLeavesTheSearchWithoutOne()
    {
        Assert.Null(ProgrammeSearchQuery.Read("keyword=news")!.System);
        Assert.Null(ProgrammeSearchQuery.Read("keyword=news&type=")!.System);
        Assert.Equal(TuneSystem.IsdbT, ProgrammeSearchQuery.Read("keyword=news&type=isdbT")!.System);
    }

    private static string Spelt(ProgrammeSearch? search)
        => search is null
            ? "nothing"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{string.Join(',', search.Words)}|{string.Join(',', search.ExcludedWords)}|{string.Join(',', search.Fields)}|{string.Join(',', search.Genres)}|{search.System}|{string.Join(',', search.Channels)}|{search.From}|{search.To}|{search.Sort}|{search.Descending}|{search.Page}|{search.PerPage}");
}
