using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeMarksTests
{
    private static readonly DateTime At = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AMarkInTheNameIsAMarkTheProgrammeCarries()
        => Assert.Equal([ProgrammeMark.New], ProgrammeMarks.In("\U0001F21Fアニメ", "あらすじ"));

    [Fact]
    public void AMarkInTheSummaryIsAMarkTheProgrammeCarriesToo()
        => Assert.Equal([ProgrammeMark.Rerun], ProgrammeMarks.In("アニメ", "\U0001F21E第1話"));

    [Fact]
    public void TheLetterAMarkFoldsIntoIsNotTheMark()
        => Assert.Empty(ProgrammeMarks.In("新番組の紹介", "再会と再生 [新] [再]"));

    [Fact]
    public void MarksAreNamedInTheOrderTheTableHoldsThemWhereverTheyWereWritten()
        => Assert.Equal(
            [ProgrammeMark.Captioned, ProgrammeMark.Rerun, ProgrammeMark.New],
            ProgrammeMarks.In("\U0001F21E\U0001F21Fドラマ", "\U0001F211"));

    [Fact]
    public void AMarkWrittenInBothPlacesIsOneMark()
        => Assert.Equal(
            [ProgrammeMark.Final],
            ProgrammeMarks.In("\U0001F221ドラマ\U0001F221", "\U0001F221"));

    [Theory]
    [InlineData(0x1F200)]
    [InlineData(0x3299)]
    [InlineData(0x2491)]
    [InlineData(0x2B1B)]
    [InlineData(0x2B24)]
    [InlineData(0x1F240)]
    [InlineData(0x1F227)]
    [InlineData(0x1F12D)]
    [InlineData(0x1F1A0)]
    public void ASymbolThatSaysNothingAboutTheProgrammeIsNotAMark(int point)
        => Assert.Empty(ProgrammeMarks.In($"題{char.ConvertFromUtf32(point)}名", char.ConvertFromUtf32(point)));

    [Fact]
    public void EveryMarkHasOneSymbolAndNoSymbolStandsForTwoMarks()
    {
        Assert.Equal(Enum.GetValues<ProgrammeMark>(), ProgrammeMarks.Symbols.Select(symbol => symbol.Mark));
        Assert.Equal(
            ProgrammeMarks.Symbols.Count,
            ProgrammeMarks.Symbols.Select(symbol => symbol.CodePoint).Distinct().Count());
        Assert.Equal(33, ProgrammeMarks.Symbols.Count);
    }

    [Theory]
    [InlineData(ProgrammeMark.HighDefinition, 0x1F14A)]
    [InlineData(ProgrammeMark.Progressive, 0x1F13F)]
    [InlineData(ProgrammeMark.Captioned, 0x1F211)]
    [InlineData(ProgrammeMark.Bilingual, 0x1F214)]
    [InlineData(ProgrammeMark.SurroundStereo, 0x1F14D)]
    [InlineData(ProgrammeMark.BModeStereo, 0x1F131)]
    [InlineData(ProgrammeMark.News, 0x1F13D)]
    [InlineData(ProgrammeMark.ParentalLock, 0x26BF)]
    [InlineData(ProgrammeMark.Rerun, 0x1F21E)]
    [InlineData(ProgrammeMark.New, 0x1F21F)]
    [InlineData(ProgrammeMark.Premiere, 0x1F220)]
    [InlineData(ProgrammeMark.Final, 0x1F221)]
    [InlineData(ProgrammeMark.VoiceCast, 0x1F224)]
    [InlineData(ProgrammeMark.PayPerView, 0x1F14E)]
    public void EachMarkIsReadFromTheSymbolTheStandardGivesIt(ProgrammeMark mark, int point)
    {
        Assert.Equal(point, ProgrammeMarks.Symbols.Single(symbol => symbol.Mark == mark).CodePoint);
        Assert.Equal([mark], ProgrammeMarks.In(char.ConvertFromUtf32(point), string.Empty));
    }

    [Fact]
    public void AProgrammeCarriesTheMarksOfItsNameAndSummary()
    {
        var programme = Programme.Discover(Broadcast("\U0001F21Fアニメ", "\U0001F211"), At);

        Assert.Equal([ProgrammeMark.Captioned, ProgrammeMark.New], programme.Marks);
        Assert.Equal([ProgrammeMark.Captioned, ProgrammeMark.New], ProgrammeMatch.Of(programme).Marks);
    }

    [Fact]
    public void AProgrammeKeepsTheMarksOfTheNameItKeptWhenANamelessReadingArrives()
    {
        var programme = Programme.Discover(Broadcast("\U0001F21Fアニメ", "あらすじ"), At);

        programme.Absorb(Broadcast(string.Empty, "\U0001F21E"), At.AddHours(1));

        Assert.Equal([ProgrammeMark.Rerun, ProgrammeMark.New], programme.Marks);
    }

    [Fact]
    public void AnArchivedProgrammeIsMatchedByTheMarksOfItsNameAndSummary()
    {
        var programme = Programme.Discover(Broadcast("\U0001F221ドラマ", string.Empty), At);
        ArchivedProgramme kept = ArchivedProgramme.Of(new EndedProgramme(programme, At.AddHours(1)), At.AddDays(2))!;

        Assert.Equal([ProgrammeMark.Final], ProgrammeMatch.Of(kept).Marks);
    }

    private static ProgrammeBroadcast Broadcast(string name, string summary)
        => new(
            new ProgrammeId(new NetworkId(32739), new ServiceId(1049), new EventId(1)),
            new TransportStreamId(32739),
            At,
            At.AddMinutes(30),
            name,
            summary,
            false);
}
