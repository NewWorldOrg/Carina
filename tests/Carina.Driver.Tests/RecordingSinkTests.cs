using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class RecordingSinkTests : IDisposable
{
    private const int ChunkSize = TsPacketReader.PacketLength * 4;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly string[] OnlyTheFinalName = ["k-1.ts"];

    private readonly string root = Directory.CreateTempSubdirectory("carina-sink-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string[] Names() =>
        [
            .. Directory
                .GetFileSystemEntries(root)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];

    private static byte[] Bytes(int count, byte seed)
    {
        byte[] bytes = new byte[count];

        for (int at = 0; at < count; at++)
        {
            bytes[at] = (byte)(seed + at);
        }

        return bytes;
    }

    private TunerSession Session(ITunerDevice device, IRecordingWriter writer) =>
        new(
            SessionId.Parse("s-1"),
            SessionPurpose.Recording,
            "adapter0",
            device,
            Start,
            Start + TimeSpan.FromHours(1),
            new ManualTimeProvider(Start),
            writer,
            ChunkSize,
            recordingId: "k-1"
        );

    [Fact]
    public void TheFileIsNamedForTheRecordingAndNotForTheSessionThatWritesIt()
    {
        using var writer = new RecordingWriter(root, "k-1");

        Assert.Equal(Path.Combine(root, "k-1.ts"), writer.Path);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("sub/k-1")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(null)]
    public void ARecordingIdThatCouldNameSomethingOutsideTheOutputRootIsNoFileName(string? recordingId)
    {
        Assert.Throws<ArgumentException>(() => RecordingFileName.Of(recordingId));
        Assert.Empty(Names());
    }

    [Fact]
    public void NoNameOtherThanTheFinalOneEverAppearsInTheOutputRoot()
    {
        using (var writer = new RecordingWriter(root, "k-1"))
        {
            Assert.Equal(OnlyTheFinalName, Names());

            writer.Write(Bytes(TsPacketReader.PacketLength, 1));

            Assert.Equal(OnlyTheFinalName, Names());
        }

        Assert.Equal(OnlyTheFinalName, Names());
    }

    [Fact]
    public void AWriterOpenedOverAFileThatIsAlreadyThereCarriesOnFromItsEnd()
    {
        byte[] first = Bytes(TsPacketReader.PacketLength, 1);
        byte[] second = Bytes(TsPacketReader.PacketLength, 100);

        using (var writer = new RecordingWriter(root, "k-1"))
        {
            writer.Write(first);
        }

        using (var writer = new RecordingWriter(root, "k-1"))
        {
            writer.Write(second);

            Assert.Equal(second.Length, writer.BytesWritten);
        }

        byte[] both = [.. first, .. second];

        Assert.Equal(both, File.ReadAllBytes(Path.Combine(root, "k-1.ts")));
        Assert.Equal(OnlyTheFinalName, Names());
    }

    [Fact]
    public void AChunkThatIsNotAWholeNumberOfPacketsStillLandsToTheLastByte()
    {
        byte[] ragged = Bytes(TsPacketReader.PacketLength + 7, 3);

        using (var writer = new RecordingWriter(root, "k-1"))
        {
            writer.Write(ragged);

            Assert.Equal(ragged.Length, writer.BytesWritten);
        }

        Assert.Equal(ragged, File.ReadAllBytes(Path.Combine(root, "k-1.ts")));
    }

    [Fact]
    public void EveryByteIsOnDiskBeforeTheWriterIsAskedForTheNextChunk()
    {
        byte[] chunk = Bytes(TsPacketReader.PacketLength, 5);

        using var writer = new RecordingWriter(root, "k-1");

        writer.Write(chunk);

        Assert.Equal(chunk, File.ReadAllBytes(Path.Combine(root, "k-1.ts")));
    }

    [Fact]
    public void AWriteThatDidNotHappenIsNotCountedAsBytesRecorded()
    {
        byte[] chunk = Bytes(TsPacketReader.PacketLength, 9);
        var writer = new RecordingWriter(root, "k-1");

        writer.Write(chunk);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.Write(chunk));
        Assert.Equal(chunk.Length, writer.BytesWritten);
        Assert.Equal(writer.BytesWritten, new FileInfo(Path.Combine(root, "k-1.ts")).Length);
    }

    [Fact]
    public void AShortReadIsWrittenWholeAndTheNextChunkFollowsItByteForByte()
    {
        byte[] ragged = Bytes(TsPacketReader.PacketLength - 38, 1);
        byte[] whole = Bytes(TsPacketReader.PacketLength, 60);

        using (var writer = new RecordingWriter(root, "k-1"))
        {
            writer.Write(ragged);
            writer.Write(whole);
        }

        byte[] both = [.. ragged, .. whole];

        Assert.Equal(both, File.ReadAllBytes(Path.Combine(root, "k-1.ts")));
    }

    [Fact]
    public void EveryRecordingIdTheContractAcceptsNamesOneFileAndNothingBesideIt()
    {
        var accepted = new List<string>();

        for (int code = 1; code < 128; code++)
        {
            string candidate = $"k{(char)code}1";

            if (!WireName.IsUsable(candidate))
            {
                continue;
            }

            accepted.Add(candidate);

            string name = RecordingFileName.Of(candidate);

            Assert.Equal(name, Path.GetFileName(name));
        }

        Assert.Contains("k-1", accepted);
        Assert.DoesNotContain($"k{Path.DirectorySeparatorChar}1", accepted);
        Assert.True(
            accepted.Count > 60,
            $"Only {accepted.Count} single-character ids were accepted, so the sweep measures next to nothing."
        );
    }

    [Fact]
    public void TheDeviceIsNotReadAgainWhileTheRecordingCannotTakeWhatItWasGiven()
    {
        var device = new PacedTunerDevice();
        var writer = new StallingRecordingWriter(Path.Combine(root, "k-1.ts"), ChunkSize);
        using TunerSession session = Session(device, writer);

        session.Start();
        device.Allow(10);
        writer.AwaitStall(Deadlock);

        Assert.Equal(2, device.Reads);

        writer.LetGo();
        device.AwaitParkedBefore(11);

        Assert.Equal(10, device.Reads);

        session.Stop();
        session.WaitForEnd(Deadlock);

        Assert.Equal(device.Reads * ChunkSize, writer.BytesWritten);
    }

    [Fact]
    public void ARecordingThatWasHeldUpLosesNothingOnceItCanWriteAgain()
    {
        var device = new PacedTunerDevice();
        var writer = new StallingRecordingWriter(Path.Combine(root, "k-1.ts"), ChunkSize);
        using TunerSession session = Session(device, writer);

        session.Start();
        device.Allow(6);
        writer.AwaitStall(Deadlock);
        writer.LetGo();
        device.AwaitParkedBefore(7);
        session.Stop();
        session.WaitForEnd(Deadlock);

        Assert.Equal(6, device.Reads);
        Assert.Equal(6 * ChunkSize, writer.BytesWritten);
        Assert.Equal(writer.BytesWritten, session.BytesRecorded);
    }
}

