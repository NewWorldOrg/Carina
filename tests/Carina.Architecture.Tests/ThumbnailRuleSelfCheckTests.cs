namespace Carina.Architecture.Tests;

public sealed class ThumbnailRuleSelfCheckTests
{
    public static TheoryData<string, string> EveryWayOfSayingHowARecordingEnded => new()
    {
        { "recording.Settle(outcome, size, at);", ".Settle(" },
        { "recording.Note(detail);", ".Note(" },
        { "recording.Interrupt(fault, at);", ".Interrupt(" },
        { "recording.Resume(at);", ".Resume(" },
        { "recording.Abort(at);", ".Abort(" },
        { "recording.Measure(counters, positions, null, 0, at);", ".Measure(" },
        { "recording.Extend(endsAt);", ".Extend(" },
        { "recording.Wrote(written);", ".Wrote(" },
        { "recording.Acquire(tuner);", ".Acquire(" },
        { "recording . Settle (outcome, size, at);", ".Settle(" },
    };

    public static TheoryData<string, string> EveryWayOfReachingPastTheAggregate => new()
    {
        { "typeof(Recording).GetProperty(\"Outcome\");", ".GetProperty(" },
        { "typeof(Recording).GetField(\"outcome\");", ".GetField(" },
        { "typeof(Recording).GetMethod(\"Settle\");", ".GetMethod(" },
        { "held.SetValue(recording, outcome);", ".SetValue(" },
        { "activator.CreateInstance(typeof(Recording));", ".CreateInstance(" },
        { "context.Database.ExecuteSqlRaw(\"UPDATE recording SET recording_outcome = 'Failed'\");", ".ExecuteSqlRaw(" },
        { "context.Set<Recording>().FromSqlRaw(\"SELECT 1\");", ".FromSqlRaw(" },
        { "held.Entry(recording);", ".Entry(" },
        { "held.Property(named);", ".Property(" },
        { "held.CurrentValue = DateTime.UtcNow;", ".CurrentValue" },
        { "held.OriginalValue = DateTime.UtcNow;", ".OriginalValue" },
    };

    public static TheoryData<string> EveryTypeTheTripWireWatches
    {
        get
        {
            var named = new TheoryData<string>();

            foreach (string machinery in ThumbnailRules.Machinery)
            {
                named.Add(machinery);
            }

            return named;
        }
    }

    [Theory]
    [MemberData(nameof(EveryWayOfSayingHowARecordingEnded))]
    [MemberData(nameof(EveryWayOfReachingPastTheAggregate))]
    public void DetectsThisWayOfReachingForARecordingsResultFromInsideTheFeature(string source, string named)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Thumbnails/Reaching.cs", source);

        Assert.Equal(
            [$"/Carina.Infrastructure/Thumbnails/Reaching.cs {named}"],
            ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void DetectsTheWayEveryoneWhoKnowsThisMapperWouldWriteIt()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Thumbnails/PictureWriteBack.cs",
            """
            context.Entry(recording)
                .Property(nameof(Recording.StoppedAtActual))
                .CurrentValue = DateTime.UtcNow;
            """);

        Assert.Equal(
            [
                "/Carina.Infrastructure/Thumbnails/PictureWriteBack.cs .CurrentValue",
                "/Carina.Infrastructure/Thumbnails/PictureWriteBack.cs .Entry(",
                "/Carina.Infrastructure/Thumbnails/PictureWriteBack.cs .Property(",
            ],
            ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void DetectsAFileNamedForThumbnailsThatSitsBesideTheFeatureRatherThanInsideIt()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/ThumbnailSupport/RecordingTouch.cs",
            "recording.Settle(outcome, size, at);");

        Assert.Equal(
            ["/Carina.Infrastructure/ThumbnailSupport/RecordingTouch.cs .Settle("],
            ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void DetectsAFileWhoseOwnNameIsTheOnlyThingSayingItIsAboutThumbnails()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/ThumbnailWriteBack.cs", "recording.Abort(at);");

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/ThumbnailWriteBack.cs .Abort("],
            ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void CannotSeeAHelperWhoseNameSaysNothingAboutThumbnails()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingTouch.cs", "recording.Settle(outcome, size, at);");

        Assert.Empty(ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void LeavesTheOneCallTheFeatureIsThereToMake()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Thumbnails/ThumbnailWriter.cs",
            "recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.TimedOut);");

        Assert.Empty(ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Fact]
    public void LeavesTheCompletionPathWritingItsOwnResult()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Recordings/RecordingCompletion.cs",
            "recording.Settle(outcome, size, at);");

        Assert.Empty(ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryTypeTheTripWireWatches))]
    public void DetectsSomethingOutsideTheFeatureNamingThisPartOfTheMachinery(string machinery)
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Recordings/RecordingCompletion.cs",
            $"internal sealed class RecordingCompletion({machinery} reaching);");

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingCompletion.cs"],
            ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void DetectsSomethingBesideTheFeatureFolderNamingTheMachineryRatherThanReadingItAsInside()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/ThumbnailSupport/Reaching.cs",
            "internal sealed class Reaching(IThumbnailRenderer renderer);");

        Assert.Equal(
            ["/Carina.Infrastructure/ThumbnailSupport/Reaching.cs"],
            ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void LeavesTheLedgerVocabularyToTheLedger()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Domain/Recordings/Recording.cs",
            "public ThumbnailState ThumbnailState { get; } public ThumbnailFault? ThumbnailFault { get; }");

        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void LeavesTheFeatureTalkingToItself()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Thumbnails/ThumbnailJob.cs",
            "internal sealed class ThumbnailJob(IThumbnailRenderer renderer);");

        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void LeavesTheTwoPlacesTheFeatureIsBuilt()
    {
        using var tree = new SourceTree();

        foreach (string allowed in ThumbnailRules.AllowedToNameTheMachinery)
        {
            tree.Write(allowed.TrimStart('/'), "internal sealed class Built(IThumbnailRenderer renderer);");
        }

        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void TheTypeScanSeesEveryShapeAFeatureFileCanDeclare()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Thumbnails/Shapes.cs",
            """
            public sealed class Drawn { }
            public sealed record Asked { }
            public readonly record struct Placed { }
            public enum Wanted { One }
            public interface IAsked { }
            public delegate Task Drawing(int width);
            public static class Helping { }
            """);

        Assert.Equal(
            ["Asked", "Drawing", "Drawn", "Helping", "IAsked", "Placed", "Wanted"],
            ThumbnailRules.TypesTheFeatureDeclares(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();

        Assert.Empty(ThumbnailRules.FilesInTheFeature(tree.Root));
        Assert.Empty(ThumbnailRules.TypesTheFeatureDeclares(tree.Root));
        Assert.Empty(ThumbnailRules.FilesNamedForThumbnails(tree.Root));
        Assert.Empty(ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(tree.Root));
        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-thumbnail-rules-");

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
