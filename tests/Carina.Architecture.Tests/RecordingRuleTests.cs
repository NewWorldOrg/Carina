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
        Assert.Equal(
            [
                "/Recordings/",
                "/Migration/",
                "/Carina.Infrastructure/Persistence/Repositories/ReservationRecordingContract.cs",
            ],
            ReservationRules.AllowedToWriteThem);
        Assert.Empty(ReservationRules.WritersOfWhatRecordingOwns(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheSchemaLevelWriterIsTheProjectionAndOnlyTheProjection()
    {
        IReadOnlyList<string> installing = SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            ReservationRules.ProjectionTrigger);

        Assert.Equal(
            [
                "Carina.Db/Migrations/20260824013352_Recordings.cs",
                ReservationRules.GuardDefinition.TrimStart('/'),
            ],
            installing);
    }

    [Fact]
    public void TheOneFileOutsideThoseTwoFoldersIsTheClaimTheContractMakes()
    {
        string contract = Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Persistence",
            "Repositories",
            "ReservationRecordingContract.cs");

        Assert.True(File.Exists(contract));
        Assert.Contains("started_at", File.ReadAllText(contract), StringComparison.Ordinal);
    }
}
