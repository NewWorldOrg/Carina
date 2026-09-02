namespace Carina.Architecture.Tests;

public sealed class StreamingRuleTests
{
    [Fact]
    public void NothingInTheStreamingFeatureTakesTheTransportStreamApart()
    {
        Assert.Empty(StreamingRules.WhatTakesTheStreamApartInsideTheFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheGlobalParserRuleAsksForTwoMarksAndTheStreamingFeatureIsAllowedNone()
    {
        Assert.Equal(2, MeasurementRules.MarksThatMakeAParser);
        Assert.Equal(1, StreamingRules.MarksThatMakeAParserHere);
    }

    [Fact]
    public void TheStreamingFeatureOpensTheDriversStreamInAtMostOnePlaceAndOnlyAsTheViewer()
    {
        IReadOnlyList<string> opening = StreamingRules.FilesOpeningTheDriversStream(RepositoryLayout.SourceDirectory);

        Assert.True(
            opening.Count <= 1,
            $"the driver's stream is opened from {opening.Count} files of the streaming feature: {string.Join(", ", opening)}");

        foreach (string relative in opening)
        {
            string source = File.ReadAllText(Path.Combine(RepositoryLayout.SourceDirectory, relative.TrimStart('/')));

            Assert.Equal(1, StreamingRules.TimesTheDriversStreamIsOpenedIn(source));
            Assert.Contains(StreamingRules.TheViewersSeat, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingInTheStreamingFeatureAsksTheDriverForAnotherSeatOrSpellsTheStreamPathByHand()
    {
        Assert.Empty(StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOtherSeatsAreStillTakenOutsideTheFeatureSoRefusingThemHereGuardsSomething()
    {
        Assert.NotEmpty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Infrastructure", "Collection"),
            "SurveySubscriber"));
        Assert.Equal(
            ["Carina.Contracts/DriverEndpoints.cs"],
            SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "public const string ViewerSubscriber"));
    }

    [Fact]
    public void NothingInTheStreamingFeatureWritesWhatBelongsToRecordingTheTunersOrTheGuide()
    {
        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheWritersTheRuleNamesAreOnDiskForItToHaveFound()
    {
        Assert.All(
            StreamingRules.WritersOfWhatIsNotStreamings,
            writer => Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, writer)));
    }

    [Fact]
    public void TheVerbsTheRuleNamesAreStillHowTheRepositoriesWrite()
    {
        string recordings = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Domain",
            "Recordings",
            "IRecordingRepository.cs"));
        string directory = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Domain",
            "Recordings",
            "IRecordingDirectory.cs"));
        string programmes = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Domain",
            "Programmes",
            "IProgrammeRepository.cs"));
        string driver = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Domain",
            "Driver",
            "IDriverClient.cs"));

        Assert.Contains("Task AddAsync(", recordings, StringComparison.Ordinal);
        Assert.Contains("Task SaveAsync(", recordings, StringComparison.Ordinal);
        Assert.Contains("HaltAsync(", directory, StringComparison.Ordinal);
        Assert.Contains("DiscardAsync(", directory, StringComparison.Ordinal);
        Assert.Contains("ForgetAsync(", programmes, StringComparison.Ordinal);
        Assert.Contains("ForgetEverythingAsync(", programmes, StringComparison.Ordinal);
        Assert.Contains("EraseRecordingAsync(", driver, StringComparison.Ordinal);
        Assert.Contains("ReplaceTunerLedgerAsync(", driver, StringComparison.Ordinal);
        Assert.Contains("ToggleTunerAsync(", driver, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStreamingFeatureIsOnDiskForTheseRulesToRead()
    {
        IReadOnlyList<string> feature = StreamingRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Api/Live/LiveWire.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Api/Playback/PlayDelivery.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Api/Controllers/Videos/IssueVideoTicketAction.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Streaming/LiveTranscoder.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Api/Services/PlaybackService.cs", feature, StringComparer.Ordinal);
        Assert.DoesNotContain("/Carina.Api/Program.cs", feature, StringComparer.Ordinal);
        Assert.DoesNotContain(
            "/Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
            feature,
            StringComparer.Ordinal);
        Assert.True(feature.Count >= 80, $"the rules read {feature.Count} file(s) of the streaming feature");
    }

    [Fact]
    public void TheEdgeIdentityRuleReadsTheSameTreeAndReachesTheStreamingFeature()
    {
        Assert.Contains(
            "Carina.Api/Live/LiveWire.cs",
            SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "namespace Carina.Api.Live;"),
            StringComparer.Ordinal);
        Assert.Contains(
            "Carina.Api/Playback/VideoDelivery.cs",
            SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "namespace Carina.Api.Playback;"),
            StringComparer.Ordinal);
    }
}
