using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeMarkMatchingTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AMarkAskedForTakesTheProgrammesThatCarryIt()
    {
        ProgrammeSearch asked = Asking(marks: [ProgrammeMark.New]);

        Assert.True(Matches("\U0001F21Fアニメ", string.Empty, asked));
        Assert.False(Matches("アニメ", string.Empty, asked));
    }

    [Fact]
    public void AMarkInTheSummaryIsTakenToo()
        => Assert.True(Matches("アニメ", "\U0001F21F", Asking(marks: [ProgrammeMark.New])));

    [Fact]
    public void TheLetterAMarkFoldsIntoIsNotTakenForTheMark()
        => Assert.False(Matches("新番組 [新]", "新", Asking(marks: [ProgrammeMark.New])));

    [Fact]
    public void EveryMarkAskedForHasToBeCarriedAsEveryWordAskedForDoes()
    {
        ProgrammeSearch asked = Asking(marks: [ProgrammeMark.New, ProgrammeMark.Captioned]);

        Assert.True(Matches("\U0001F21Fアニメ", "\U0001F211", asked));
        Assert.False(Matches("\U0001F21Fアニメ", string.Empty, asked));
        Assert.False(Matches("\U0001F211アニメ", string.Empty, asked));
    }

    [Fact]
    public void AnyOneOfTheMarksLeftOutLeavesTheProgrammeOut()
    {
        ProgrammeSearch asked = Asking(excluded: [ProgrammeMark.Rerun, ProgrammeMark.Final]);

        Assert.False(Matches("\U0001F21Eアニメ", string.Empty, asked));
        Assert.False(Matches("アニメ", "\U0001F221", asked));
        Assert.True(Matches("\U0001F21Fアニメ 再会", string.Empty, asked));
    }

    [Fact]
    public void AMarkAskedForAndAnotherLeftOutAreBothHeld()
    {
        ProgrammeSearch asked = Asking(marks: [ProgrammeMark.New], excluded: [ProgrammeMark.Rerun]);

        Assert.True(Matches("\U0001F21Fアニメ", string.Empty, asked));
        Assert.False(Matches("\U0001F21F\U0001F21Eアニメ", string.Empty, asked));
    }

    [Fact]
    public void WhereTheWordsAreLookedForDoesNotNarrowWhereTheMarksAreRead()
    {
        ProgrammeSearch asked = ProgrammeSearch.For(
            "アニメ",
            null,
            null,
            conditions: new ProgrammeConditions
            {
                Fields = [ProgrammeField.Title],
                Marks = [ProgrammeMark.New],
            })!;

        Assert.True(Matches("アニメ", "\U0001F21F", asked));
    }

    private static ProgrammeSearch Asking(
        IReadOnlyList<ProgrammeMark>? marks = null,
        IReadOnlyList<ProgrammeMark>? excluded = null)
        => ProgrammeSearch.For(
            null,
            null,
            null,
            conditions: new ProgrammeConditions { Marks = marks, ExcludedMarks = excluded })!;

    private static bool Matches(string name, string summary, ProgrammeSearch search)
        => ProgrammeSearchMatching.Matches(
            ProgrammeMatch.Of(Programme.Discover(
                new ProgrammeBroadcast(
                    new ProgrammeId(new NetworkId(32739), new ServiceId(1049), new EventId(1)),
                    new TransportStreamId(32739),
                    Now.AddHours(1),
                    Now.AddHours(2),
                    name,
                    summary,
                    false),
                Now)),
            search,
            Now);
}
