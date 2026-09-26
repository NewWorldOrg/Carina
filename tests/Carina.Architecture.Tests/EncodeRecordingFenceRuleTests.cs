namespace Carina.Architecture.Tests;

public sealed class EncodeRecordingFenceRuleTests
{
    [Fact(DisplayName = "nothing in the encode feature writes the recording a job was made from")]
    public void NothingInTheEncodeFeatureWritesTheRecordingAJobWasMadeFrom()
        => Assert.Empty(EncodeRecordingFenceRules.WhatWritesTheRecordingItWasMadeFrom(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "the rule reads the run, the restart, the call-off and the surface that offers it")]
    public void TheRuleReadsTheRunTheRestartTheCallOffAndTheSurfaceThatOffersIt()
    {
        IReadOnlyList<string> read = EncodeRecordingFenceRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeJobRunner.cs", read);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeRestart.cs", read);
        Assert.Contains("/Carina.Api/Services/EncodeJobService.cs", read);
        Assert.Contains("/Carina.Api/Controllers/Encoding/CancelEncodeJobAction.cs", read);
    }

    [Fact(DisplayName = "the one place in the feature that holds the recording port is the run, which only reads it")]
    public void TheOnePlaceInTheFeatureThatHoldsTheRecordingPortIsTheRun()
        => Assert.Equal(
            ["/Carina.Infrastructure/Encodings/EncodeJobRunner.cs"],
            EncodeRecordingFenceRules.HoldersOfTheRecordingPort(RepositoryLayout.SourceDirectory));
}
