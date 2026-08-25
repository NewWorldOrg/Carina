namespace Carina.Architecture.Tests;

public sealed class MeasurementRuleSelfCheckTests
{
    private const string Anchor = "private const byte Sync = 0x47;";

    private const string SecondAnchor = "private const int Stride = 188;";

    public static TheoryData<string, string, string> EachMarkBesideAnother() =>
        new()
        {
            { "names", "private readonly TsPacketReader reader = new();", Anchor },
            { "sync", "private const byte Marker = 0x47;", SecondAnchor },
            { "stride", "private const int Length = 188;", Anchor },
            { "payload", "private const int Body = 184;", Anchor },
            { "pid", "private const int Mask = 0x1FFF;", Anchor },
            { "counter", "private const int Sequence = 0xF;", Anchor },
        };

    [Theory]
    [MemberData(nameof(EachMarkBesideAnother))]
    public void EveryMarkIsWhatMakesTheDifferenceForAtLeastOneParser(
        string mark,
        string writes,
        string beside
    )
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-mark-");

        try
        {
            Write(directory, $"Carina.Driver/Recording/{mark}.cs", Parser(writes, beside));
            Write(directory, $"Carina.Driver/Recording/only-{mark}.cs", Parser(beside));

            Assert.Equal(
                [$"Carina.Driver/Recording/{mark}.cs"],
                MeasurementRules.PlacesThatShowTheMarksOfAParser(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AProjectWhoseTradeIsParsingIsExcusedByNameRatherThanByWhereItSits()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-trade-");

        try
        {
            Write(directory, "Carina.Broadcast/Reader.cs", Parser(Anchor, SecondAnchor));
            Write(directory, "Carina.Contracts/Reader.cs", Parser(Anchor, SecondAnchor));

            Assert.Equal(
                ["Carina.Contracts/Reader.cs"],
                MeasurementRules.PlacesThatShowTheMarksOfAParser(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AMaskThatMerelyLooksLikeOneOfThemIsNotAParser()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-innocent-");

        try
        {
            Write(
                directory,
                "Carina.Driver/Ipc/Permissions.cs",
                Parser(
                    "private const uint Mode = 0x0FFF;",
                    "private const int RetryAfterMilliseconds = 188;"));

            Assert.Empty(MeasurementRules.PlacesThatShowTheMarksOfAParser(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AParserThatWritesNoneOfTheMarksWalksPastThisRule()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-quiet-");

        try
        {
            Write(
                directory,
                "Carina.Driver/Recording/QuietAudit.cs",
                """
                namespace Sample;
                public sealed class QuietAudit
                {
                    private const int Stride = 4 + 180 + 4;
                    private const byte Marker = 0x40 + 0x07;

                    public long Lost { get; private set; }

                    public void Take(byte[] chunk)
                    {
                        for (int at = 0; at + Stride <= chunk.Length; at += Stride)
                        {
                            if (chunk[at] == Marker)
                            {
                                Lost++;
                            }
                        }
                    }
                }
                """);

            Assert.Empty(MeasurementRules.PlacesThatShowTheMarksOfAParser(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectsASecondPlaceThatCountsAndLeavesTheDefinitionAlone()
    {
        DirectoryInfo directory = Counting();

        try
        {
            Assert.Equal(
                [
                    "Carina.Driver/Recording/RecordingSink.cs",
                    "Carina.Driver/Sessions/TunerSession.cs",
                ],
                MeasurementRules.PlacesThatCountWhatTheTunerGave(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectsAWriterThatReadsPacketsAndLeavesAByteSinkAlone()
    {
        DirectoryInfo directory = Counting();

        try
        {
            Assert.Equal(
                ["Carina.Driver/Recording/RecordingSink.cs"],
                MeasurementRules.PacketsReadInsideTheRecordingWriter(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string Parser(params string[] lines) =>
        "namespace Sample;\npublic sealed class Sample\n{\n    "
        + string.Join("\n    ", lines)
        + "\n}\n";

    private static DirectoryInfo Counting()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-measurement-");

        Write(
            directory,
            "Carina.Driver/Transport/ContinuityCounterTracker.cs",
            """
            namespace Sample;
            public sealed class ContinuityCounterTracker
            {
                public void Observe(int counter) { }
            }
            """);
        Write(
            directory,
            "Carina.Driver/Sessions/TunerSession.cs",
            """
            namespace Sample;
            public sealed class TunerSession
            {
                public ContinuityCounterTracker Counters { get; } = new();
            }
            """);
        Write(
            directory,
            "Carina.Driver/Recording/RecordingSink.cs",
            """
            namespace Sample;
            public sealed class RecordingSink
            {
                private readonly ContinuityCounterTracker counters = new();

                public void Write(byte[] chunk) => counters.Observe(chunk.Length);
            }
            """);
        Write(
            directory,
            "Carina.Driver/Recording/RecordingWriter.cs",
            """
            namespace Sample;
            public sealed class RecordingWriter
            {
                public void Write(byte[] chunk) { }
            }
            """);

        return directory;
    }

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
