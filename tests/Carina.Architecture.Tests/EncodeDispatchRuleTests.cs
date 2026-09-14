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

    private static string Invocation => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "FfmpegEncodeInvocation.cs");

    private static string Looking => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "FfmpegChapterInvocation.cs");

    private static string Look => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "FfmpegChapterRun.cs");

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

    [Fact(DisplayName = "BR-ED2-011: the encode feature starts a programme in four places — the run, which hands the ledger the programme's identity, the look for the breaks, and the two probes of the source's head and length, each bounded by a deadline and unable to outlive the process by more than that — and nowhere else")]
    public void TheEncodeFeatureStartsAProgrammeInFourPlacesAndNowhereElse()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Encodings/FfmpegChapterRun.cs AnotherProgramme.Start(",
                "/Carina.Infrastructure/Encodings/FfmpegEncodeRun.cs AnotherProgramme.Start(",
                "/Carina.Infrastructure/Encodings/FfprobeSourceHead.cs AnotherProgramme.SayAsync(",
                "/Carina.Infrastructure/Encodings/FfprobeSourceLength.cs AnotherProgramme.SayAsync(",
            ],
            EncodeDispatchRules.WhatStartsAProgramme(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-006: the encode moves the clock in one place only, the -ss its invocation writes after the input; the look for the breaks seeks before its input and keeps the source's own clock, and nothing in the feature spells -output_ts_offset, -start_at_zero, -avoid_negative_ts or -itsoffset")]
    public void TheOnlyPlacesTheEncodeFeatureMovesTheClockAreTheTwoItBuildsCommandsIn()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Encodings/FfmpegChapterInvocation.cs \"-copyts\"",
                "/Carina.Infrastructure/Encodings/FfmpegChapterInvocation.cs \"-ss\"",
                "/Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs \"-ss\"",
            ],
            EncodeDispatchRules.WhatMovesTheClock(RepositoryLayout.SourceDirectory));

        string source = File.ReadAllText(Invocation);
        int input = source.IndexOf("\"-i\",", StringComparison.Ordinal);
        int skip = source.IndexOf("\"-ss\",", StringComparison.Ordinal);

        Assert.True(input >= 0 && skip > input, "the skip is written after the input, as a trim, not before it as a seek");
        Assert.Equal(1, source.Split("\"-ss\"").Length - 1);

        string peeking = File.ReadAllText(Looking);
        string body = peeking[peeking.IndexOf("Peeking(", StringComparison.Ordinal)..];
        int read = body.IndexOf("\"-i\",", StringComparison.Ordinal);
        int seek = body.IndexOf("\"-ss\",", StringComparison.Ordinal);

        Assert.True(seek >= 0 && read > seek, "the look seeks before its input, so only the seconds it asked for are decoded");
        Assert.Equal(1, peeking.Split("\"-ss\"").Length - 1);
        Assert.Equal(1, peeking.Split("\"-copyts\"").Length - 1);
    }

    [Fact(DisplayName = "BR-ED2-011: the look hands over who its programme is before it reads a line of either stream, and stops the programme when that cannot be written down")]
    public void TheLookHandsOverWhoItsProgrammeIsBeforeItReadsALine()
    {
        string source = File.ReadAllText(Look);
        int started = source.IndexOf("AnotherProgramme.Start(programme, arguments, ProgrammePriority.Yielding)", StringComparison.Ordinal);
        int handedOver = source.IndexOf("await began(spawned);", StringComparison.Ordinal);
        int stoppedInstead = source.IndexOf("AnotherProgramme.GiveUpOn(running);\n\n                throw;", StringComparison.Ordinal);
        int read = source.IndexOf("ReadAsync(running.Standard", StringComparison.Ordinal);

        Assert.True(started >= 0, "the programme is started yielding");
        Assert.True(handedOver > started, "the identity is handed over after the start");
        Assert.True(stoppedInstead > handedOver, "a hand-over that fails stops the programme");
        Assert.True(read > handedOver, "neither stream is read until the identity is handed over");
    }

    [Fact(DisplayName = "BR-ED2-005: the look for the breaks starts its programme yielding, reads both of its streams as they come, and starts nothing else")]
    public void TheLookForTheBreaksStartsItsProgrammeYielding()
    {
        string source = File.ReadAllText(Look);

        Assert.Contains(
            "AnotherProgramme.Start(programme, arguments, ProgrammePriority.Yielding)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("running.StandardError", source, StringComparison.Ordinal);
        Assert.Contains("running.StandardOutput", source, StringComparison.Ordinal);
        Assert.Contains("ReadLineAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadToEndAsync(", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("AnotherProgramme.Start(").Length - 1);
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
