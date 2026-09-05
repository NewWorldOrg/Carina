namespace Carina.Architecture.Tests;

public sealed class EncodeDispatchRuleSelfCheckTests
{
    private const string InTheFolder = "Carina.Infrastructure/Encodings/EncodeStarter.cs";

    private const string NamedForIt = "Carina.Api/Services/EncodeService.cs";

    public static TheoryData<string, string> EveryOrdinaryWayOfMovingAJobToRunning => new()
    {
        { """job.Status = EncodeJobStatus.Running;""", "=EncodeJobStatus.Running" },
        { """Status = EncodeJobStatus.Running,""", "=EncodeJobStatus.Running" },
        { """await context.Set<EncodeJob>().Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, EncodeJobStatus.Running), ct);""", "SetProperty(row=>row.Status,EncodeJobStatus.Running" },
    };

    public static TheoryData<string> EveryWayOfLookingAtRunningThatIsNotAMove =>
    [
        """if (job.Status == EncodeJobStatus.Running) return;""",
        """if (job.Status != EncodeJobStatus.Running) throw;""",
        """bool running = job.Status is EncodeJobStatus.Running;""",
        """.Where(row => row.Status == EncodeJobStatus.Running)""",
        """case EncodeJobStatus.Running: break;""",
        """EncodeJobStatus.Running => EncodeStanding.Running,""",
    ];

    public static TheoryData<string> EveryWayOfMovingAJobThatWalksStraightPast =>
    [
        """job.Start(now);""",
        """await context.Database.ExecuteSqlRawAsync(sql);""",
        """typeof(EncodeJob).GetProperty("Status")!.SetValue(job, running);""",
    ];

    public static TheoryData<string, string> EveryOrdinaryWayOfPuttingAFileSomewhere => new()
    {
        { """File.Move(work, artefact);""", "File.Move" },
        { """File.Copy(work, artefact, overwrite: true);""", "File.Copy" },
        { """File.Replace(work, artefact, null);""", "File.Replace" },
        { """File.CreateSymbolicLink(artefact, work);""", "File.CreateSymbolicLink" },
        { """new FileInfo(work).MoveTo(artefactPath);""", ".MoveTo(artefactPath" },
        { """new FileInfo(work).CopyTo(destinationPath, true);""", ".CopyTo(destinationPath" },
        { """rename(work, artefact);""", "rename(" },
    };

    public static TheoryData<string> EveryWayOfPuttingAFileThatWalksStraightPast =>
    [
        """AnotherProgramme.Describe("mv", [work, artefact]);""",
        """using FileStream copy = File.Create(artefact); await source.CopyToAsync(copy);""",
        """buffer.CopyTo(span);""",
    ];

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfMovingAJobToRunning))]
    public void DetectsThisWayOfMovingAJobToRunning(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Equal([$"/{InTheFolder} {reported}"], EncodeDispatchRules.WhatMovesAJobToRunning(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfLookingAtRunningThatIsNotAMove))]
    public void DoesNotReportLookingAtRunningAsAMove(string source)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Empty(EncodeDispatchRules.WhatMovesAJobToRunning(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfMovingAJobThatWalksStraightPast))]
    public void CannotSeeThisWayOfMovingAJob(string source)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Empty(EncodeDispatchRules.WhatMovesAJobToRunning(tree.Root));
    }

    [Fact]
    public void DetectsTheDatabaseSpellingOfRunning()
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, """await context.Database.ExecuteSqlRawAsync("UPDATE encode_job SET status = 'Running' WHERE id = {0}", id);""");

        Assert.Equal([$"/{InTheFolder} 'Running'"], EncodeDispatchRules.WhatSpellsRunningForTheDatabase(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfPuttingAFileSomewhere))]
    public void DetectsThisWayOfPuttingAFileSomewhere(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Equal([$"/{InTheFolder} {reported}"], EncodeDispatchRules.WhatPutsAFileSomewhere(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfPuttingAFileThatWalksStraightPast))]
    public void CannotSeeThisWayOfPuttingAFileSomewhere(string source)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Empty(EncodeDispatchRules.WhatPutsAFileSomewhere(tree.Root));
    }

    [Fact]
    public void DetectsTheArtefactBeingNamedAnywhereInTheFeature()
    {
        using var tree = new SourceTree();
        tree.Write(NamedForIt, """string artefact = Path.Combine(room, EncodeFileName.Artefact(recording, profile).Value);""");

        Assert.Equal([$"/{NamedForIt} EncodeFileName.Artefact("], EncodeDispatchRules.WhatNamesTheArtefact(tree.Root));
    }

    [Fact]
    public void ReadsAFileNamedForTheFeatureWhereverItSits()
    {
        using var tree = new SourceTree();
        tree.Write(NamedForIt, """job.Status = EncodeJobStatus.Running;""");
        tree.Write("Carina.Api/Services/RecordingService.cs", """job.Status = EncodeJobStatus.Running; File.Move(a, b);""");

        Assert.Equal([$"/{NamedForIt} =EncodeJobStatus.Running"], EncodeDispatchRules.WhatMovesAJobToRunning(tree.Root));
        Assert.Empty(EncodeDispatchRules.WhatPutsAFileSomewhere(tree.Root));
        Assert.Equal([$"/{NamedForIt}"], EncodeDispatchRules.FilesInTheFeature(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-dispatch-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
