namespace Carina.Architecture.Tests;

public sealed class MeasurementRuleTests
{
    [Fact]
    public void NothingHereCountsWhatTheTunerGaveBecauseTheDriverCountsAsItWrites()
    {
        Assert.Empty(MeasurementRules.PlacesThatCountWhatTheTunerGave(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void NothingOutsideTheBroadcastLibraryShowsTheMarksOfAParser()
    {
        Assert.Empty(MeasurementRules.PlacesThatShowTheMarksOfAParser(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOnlyProjectExcusedFromThatIsTheOneWhoseTradeIsParsing()
    {
        Assert.Equal(["Carina.Broadcast"], MeasurementRules.ParsersByTrade);
        Assert.True(
            MeasurementRules.MarksIn(
                File.ReadAllText(Path.Combine(
                    RepositoryLayout.SourceDirectory,
                    "Carina.Broadcast",
                    "Sections",
                    "TransportPacket.cs")))
                >= MeasurementRules.MarksThatMakeAParser,
            "The excused project no longer looks like a parser, so excusing it guards nothing.");
    }
}
