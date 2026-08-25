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
    public void EveryPlaceThatTakesTheStreamApartIsOneOfTheFourNamedHere()
    {
        Assert.Equal(
            MeasurementRules.AllowedToTakeTheStreamApart,
            MeasurementRules.PlacesThatTakeTheStreamApart(RepositoryLayout.SourceDirectory));
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
                "*.cs"));
    }
}
