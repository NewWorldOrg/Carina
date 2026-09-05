namespace Carina.Architecture.Tests;

public sealed class EncodeDispatchRuleTests
{
    private static string Repository => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Persistence",
        "Repositories",
        "EncodeJobRepository.cs");

    private static string Run => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "FfmpegEncodeRun.cs");

    private static string Placer => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "EncodeArtefactPlacer.cs");

    [Fact(DisplayName = "BR-ED2-005: the two places a job is moved to running are the entity's own move and the ledger's conditional update, and nothing beside them")]
    public void TheTwoPlacesAJobIsMovedToRunningAreTheEntityAndTheLedgersConditionalUpdate()
    {
        Assert.Equal(
            [
                "/Carina.Domain/Encodings/EncodeJob.cs =EncodeJobStatus.Running",
                "/Carina.Infrastructure/Persistence/Repositories/EncodeJobRepository.cs SetProperty(row=>row.Status,EncodeJobStatus.Running",
            ],
            EncodeDispatchRules.WhatMovesAJobToRunning(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-005: the ledger's move to running changes a row only while it is still queued, and a second running row is read off the unique index")]
    public void TheLedgersMoveToRunningIsConditionalAndReadsTheIndex()
    {
        string source = File.ReadAllText(Repository);
        int conditional = source.IndexOf(".Where(row => row.Id == next && row.Status == EncodeJobStatus.Queued)", StringComparison.Ordinal);
        int update = source.IndexOf(".SetProperty(row => row.Status, EncodeJobStatus.Running)", StringComparison.Ordinal);

        Assert.True(conditional >= 0, "the update is conditional on the row still being queued");
        Assert.True(update > conditional, "the condition is written before the move");
        Assert.Contains("if (written is 0)", source, StringComparison.Ordinal);
        Assert.Contains("ConstraintName: EncodeJobConfiguration.RunningIndexName", source, StringComparison.Ordinal);
        Assert.Contains("return EncodeClaim.AnotherIsRunning();", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Start(", source, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-005: the word the database knows a running job by is spelt in the table's configuration and nowhere else in the feature")]
    public void TheWordTheDatabaseKnowsARunningJobByIsSpeltInTheConfigurationAlone()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Configurations/EncodeJobConfiguration.cs 'Running'"],
            EncodeDispatchRules.WhatSpellsRunningForTheDatabase(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-009: the one place the encode feature puts a file somewhere is the placer, and it writes the ledger before it moves anything")]
    public void TheOnePlaceTheEncodeFeaturePutsAFileSomewhereIsThePlacer()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Encodings/EncodeArtefactPlacer.cs File.Move"],
            EncodeDispatchRules.WhatPutsAFileSomewhere(RepositoryLayout.SourceDirectory));

        string source = File.ReadAllText(Placer);
        int claimed = source.IndexOf("jobs.ClaimArtefactAsync(job, candidate, cancellationToken)", StringComparison.Ordinal);
        int moved = source.IndexOf("File.Move(work, artefact, overwrite: false)", StringComparison.Ordinal);

        Assert.True(claimed >= 0, "the placer writes the name into the ledger");
        Assert.True(moved > claimed, "the move comes after the ledger is written");
        Assert.Equal(1, source.Split("File.Move(").Length - 1);
    }

    [Fact(DisplayName = "BR-ED2-009: the artefact's name is worked out in the placer and checked by the job, and nothing else in the feature can spell it")]
    public void TheArtefactsNameIsWorkedOutInThePlacerAndCheckedByTheJob()
    {
        Assert.Equal(
            [
                "/Carina.Domain/Encodings/EncodeJob.cs EncodeFileName.Artefact(",
                "/Carina.Infrastructure/Encodings/EncodeArtefactPlacer.cs EncodeFileName.Artefact(",
            ],
            EncodeDispatchRules.WhatNamesTheArtefact(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-011: the encode feature starts a programme in two places — the run, which hands the ledger the programme's identity, and the length probe, which is bounded by a deadline and cannot outlive the process by more than that — and nowhere else")]
    public void TheEncodeFeatureStartsAProgrammeInTwoPlacesAndNowhereElse()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Encodings/FfmpegEncodeRun.cs AnotherProgramme.Start(",
                "/Carina.Infrastructure/Encodings/FfprobeSourceLength.cs AnotherProgramme.SayAsync(",
            ],
            EncodeDispatchRules.WhatStartsAProgramme(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-011: the run hands over who the programme is before it reads a line of progress, stops the programme when that cannot be written down, and starts it yielding")]
    public void TheRunHandsOverWhoTheProgrammeIsBeforeItReadsALineOfProgress()
    {
        string source = File.ReadAllText(Run);
        int started = source.IndexOf("AnotherProgramme.Start(programme, arguments, ProgrammePriority.Yielding)", StringComparison.Ordinal);
        int handedOver = source.IndexOf("await began(spawned);", StringComparison.Ordinal);
        int stoppedInstead = source.IndexOf("AnotherProgramme.GiveUpOn(running);\n\n                throw;", StringComparison.Ordinal);
        int read = source.IndexOf("StandardOutput.ReadLineAsync(", StringComparison.Ordinal);

        Assert.True(started >= 0, "the programme is started yielding");
        Assert.True(handedOver > started, "the identity is handed over after the start");
        Assert.True(stoppedInstead > handedOver, "a hand-over that fails stops the programme");
        Assert.True(read > handedOver, "progress is read only after the identity is handed over");
        Assert.Equal(1, source.Split("AnotherProgramme.Start(").Length - 1);
    }

    [Fact]
    public void TheFeatureIsOnDiskForThoseTripWiresToRead()
    {
        IReadOnlyList<string> feature = EncodeDispatchRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Domain/Encodings/EncodeJob.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeDispatch.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeJobRunner.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Persistence/Repositories/EncodeJobRepository.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Persistence/Configurations/EncodeJobConfiguration.cs", feature, StringComparer.Ordinal);
        Assert.True(feature.Count >= 40, $"the trip wires read {feature.Count} file(s) of the feature");
    }
}
