namespace Carina.Architecture.Tests;

public sealed class MeasurementRuleTests
{
    [Fact]
    public void OneLoopCountsWhatTheTunerGaveAndItIsTheOneThatWritesTheRecording()
    {
        Assert.Equal(
            ["Carina.Driver/Sessions/TunerSession.cs"],
            MeasurementRules.PlacesThatCountWhatTheTunerGave(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void EveryPlaceThatShowsTheMarksOfAParserIsOneOfTheTwoNamedHere()
    {
        Assert.Equal(
            MeasurementRules.AllowedToTakeTheStreamApart,
            MeasurementRules.PlacesThatShowTheMarksOfAParser(RepositoryLayout.SourceDirectory));
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

    [Fact]
    public void TheRecordingWriterTakesBytesAndReadsNoPacketOfItsOwn()
    {
        Assert.Empty(
            MeasurementRules.PacketsReadInsideTheRecordingWriter(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheWriterThoseRulesGuardIsOnDiskForThemToHaveRead()
    {
        Assert.NotEmpty(
            Directory.EnumerateFiles(
                Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Driver", "Recording"),
                "*.cs",
                SearchOption.AllDirectories));
    }
}
