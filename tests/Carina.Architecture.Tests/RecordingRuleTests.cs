namespace Carina.Architecture.Tests;

public sealed class RecordingRuleTests
{
    [Fact]
    public void TheRecordingFeatureReadsNoSectionOfItsOwn()
    {
        Assert.Empty(RecordingRules.EitReadersInsideTheRecordingFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheRecordingFeatureWritesNothingIntoTheGuide()
    {
        Assert.Empty(RecordingRules.GuideWritersInsideTheRecordingFeature(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheRecordingSurfaceOffersNoWayToDeleteAnything()
    {
        Assert.Empty(RecordingRules.DeleteEndpointsOnTheRecordingSurface(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheRecordingFeatureIsOnDiskForThoseRulesToRead()
    {
        IReadOnlyList<string> feature = SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            "namespace Carina.Domain.Recordings;");

        Assert.Contains("Carina.Domain/Recordings/Recording.cs", feature, StringComparer.Ordinal);
    }

    [Fact]
    public void TheGuideStillHasSomethingForThoseRulesToHaveFound()
    {
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "EventInformationTable"));
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "IProgrammeRepository"));
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "[HttpDelete]"));
    }

    [Fact]
    public void WhatRecordingOwnsOnTheReservationIsWrittenByRecordingAndByMigration()
    {
        Assert.Contains("/Recordings/", ReservationRules.AllowedToWriteThem, StringComparer.Ordinal);
        Assert.Contains("/Migration/", ReservationRules.AllowedToWriteThem, StringComparer.Ordinal);
        Assert.Empty(ReservationRules.WritersOfWhatRecordingOwns(RepositoryLayout.SourceDirectory));
    }
}
