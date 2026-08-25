namespace Carina.Architecture.Tests;

public sealed class MeasurementRuleSelfCheckTests
{
    [Fact]
    public void DetectsASecondPlaceThatCountsAndLeavesTheDefinitionAlone()
    {
        DirectoryInfo directory = Planted();

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
        DirectoryInfo directory = Planted();

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

    private static DirectoryInfo Planted()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-measurement-");

        Write(
            directory,
            "Carina.Driver/Transport/ContinuityCounterTracker.cs",
            """
            namespace Sample;
            public sealed class ContinuityCounterTracker
            {
                public void Observe(TsPacket packet) { }
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

                public void Write(byte[] chunk)
                {
                    foreach (TsPacket packet in Read(chunk))
                    {
                        counters.Observe(packet);
                    }
                }
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
