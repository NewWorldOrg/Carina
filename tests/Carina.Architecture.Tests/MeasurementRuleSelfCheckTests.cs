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

    [Fact]
    public void DetectsAReaderSuppliedFromSomewhereTheAllowListDoesNotName()
    {
        DirectoryInfo directory = Planted();

        try
        {
            Assert.Contains(
                "Transport/PacketTap.cs",
                MeasurementRules.PlacesThatTakeTheStreamApart(directory.FullName),
                StringComparer.Ordinal);
            Assert.DoesNotContain(
                "Transport/PacketTap.cs",
                MeasurementRules.AllowedToTakeTheStreamApart,
                StringComparer.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectsAParserThatNamesNoneOfThoseTypesAtAll()
    {
        DirectoryInfo directory = Planted();

        try
        {
            string audit = File.ReadAllText(
                Path.Combine(directory.FullName, "Carina.Driver", "Recording", "StreamAudit.cs"));

            Assert.DoesNotContain(MeasurementRules.Counter, audit, StringComparison.Ordinal);
            Assert.DoesNotContain(MeasurementRules.Packet, audit, StringComparison.Ordinal);
            Assert.DoesNotContain(MeasurementRules.Reader, audit, StringComparison.Ordinal);

            Assert.Contains(
                "Recording/StreamAudit.cs",
                MeasurementRules.PlacesThatTakeTheStreamApart(directory.FullName),
                StringComparer.Ordinal);
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

        Write(
            directory,
            "Carina.Driver/Transport/PacketTap.cs",
            """
            namespace Sample;
            public sealed class PacketTap
            {
                private readonly TsPacketReader reader = new();

                public IReadOnlyList<TsPacket> Read(byte[] chunk) => reader.Read(chunk);
            }
            """);

        Write(
            directory,
            "Carina.Driver/Recording/StreamAudit.cs",
            """
            namespace Sample;
            public sealed class StreamAudit
            {
                private readonly Dictionary<int, int> last = [];

                public long Lost { get; private set; }

                public void Take(byte[] chunk)
                {
                    for (int at = 0; at + 188 <= chunk.Length; at += 188)
                    {
                        if (chunk[at] != 0x47)
                        {
                            continue;
                        }

                        int pid = ((chunk[at + 1] & 0x1F) << 8) | chunk[at + 2];
                        int counter = chunk[at + 3] & 0x0F;

                        if (last.TryGetValue(pid, out int seen) && counter != (seen + 1) % 16)
                        {
                            Lost++;
                        }

                        last[pid] = counter;
                    }
                }
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
