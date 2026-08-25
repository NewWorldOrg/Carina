using Carina.Contracts;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class RecordingMeasurementTests
{
    private const int VideoPid = 0x0100;
    private const int PacketLength = 188;
    private const long Second = 90_000;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly DriverHello Hello =
        new(DriverProtocol.Version, "instance-1", DriverGreeting.Capabilities);

    private static byte[] Packet(int pid, int counter, long? pcr = null, bool scrambled = false)
    {
        byte[] packet = new byte[PacketLength];
        Array.Fill(packet, (byte)(counter + 1), 4, PacketLength - 4);

        packet[0] = TsPacketReader.SyncByte;
        packet[1] = (byte)((pid >> 8) & 0x1F);
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)(
            (scrambled ? 0xC0 : 0x00) | (pcr is null ? 0x10 : 0x30) | (counter & 0x0F)
        );

        if (pcr is not { } reference)
        {
            return packet;
        }

        packet[4] = 7;
        packet[5] = 0x10;
        packet[6] = (byte)(reference >> 25);
        packet[7] = (byte)((reference >> 17) & 0xFF);
        packet[8] = (byte)((reference >> 9) & 0xFF);
        packet[9] = (byte)((reference >> 1) & 0xFF);
        packet[10] = (byte)(((int)(reference & 1) << 7) | 0x7E);
        packet[11] = 0x00;

        return packet;
    }

    private static byte[] Broadcast(
        int packets,
        int losesAfter = -1,
        int losing = 0,
        int scramblesFrom = int.MaxValue
    )
    {
        var stream = new List<byte>();
        int counter = 0;

        for (int index = 0; index < packets; index++)
        {
            long? pcr = index % 10 is 0 ? index / 10 * Second : null;

            stream.AddRange(Packet(VideoPid, counter % 16, pcr, index >= scramblesFrom));
            counter++;

            if (index == losesAfter)
            {
                counter += losing;
            }
        }

        return [.. stream];
    }

    private static IReadOnlyList<byte[]> Ragged(byte[] stream, params int[] sizes)
    {
        var chunks = new List<byte[]>();
        int taken = 0;
        int next = 0;

        while (taken < stream.Length)
        {
            int size = Math.Min(sizes[next % sizes.Length], stream.Length - taken);

            chunks.Add(stream[taken..(taken + size)]);
            taken += size;
            next++;
        }

        return chunks;
    }

    private static TunerSession Recording(ITunerDevice device, IRecordingWriter writer) =>
        new(
            SessionId.Parse("rec-1"),
            SessionPurpose.Recording,
            "adapter0",
            device,
            Start,
            Start + TimeSpan.FromHours(1),
            new ManualTimeProvider(Start),
            writer,
            PacketLength * 100,
            outputRoot: "primary",
            recordingId: "k-90210"
        );

    private static TunerSession Ran(byte[] stream, params int[] sizes)
    {
        var device = new ScriptedBytesDevice(Ragged(stream, sizes));
        var writer = new RememberingRecordingWriter();
        TunerSession session = Recording(device, writer);

        session.Start();
        device.AwaitDrained(Deadlock);
        session.Stop();
        Assert.True(session.Completion.Wait(Deadlock), "The session never let go.");

        Assert.Equal(stream, writer.Written);

        return session;
    }

    [Fact]
    public void EveryByteTheTunerGaveUsIsWrittenAndCounted()
    {
        byte[] stream = Broadcast(300);

        using TunerSession session = Ran(stream, PacketLength * 100);

        Assert.Equal(300, session.Counters.Packets);
        Assert.Equal(0, session.Counters.Drops);
        Assert.Equal(0, session.DiscardedBytes);
        Assert.Equal(0, session.Resyncs);
        Assert.True(session.Counters.CcMeasured);
    }

    [Fact]
    public void AReadThatStopsPartWayThroughAPacketDoesNotThrowTheCountOut()
    {
        byte[] stream = Broadcast(300);

        using TunerSession session = Ran(stream, 100, 251, 37, 1024, 7);

        Assert.Equal(300, session.Counters.Packets);
        Assert.Equal(0, session.Counters.Drops);
        Assert.Equal(0, session.DiscardedBytes);
        Assert.Equal(0, session.Resyncs);
    }

    [Fact]
    public void WhatWasInjectedIsWhatIsCountedHoweverTheReadsFellAcrossIt()
    {
        byte[] stream = Broadcast(300, losesAfter: 50, losing: 3);

        using TunerSession session = Ran(stream, 100, 251, 37, 1024, 7);

        Assert.Equal(3, session.Counters.Drops);
        Assert.Equal(3, session.Counters.DropsFor(VideoPid));
    }

    [Fact]
    public void WhereItWasInjectedIsWhereItIsPlaced()
    {
        byte[] stream = Broadcast(300, losesAfter: 50, losing: 3);

        using TunerSession session = Ran(stream, 100, 251, 37, 1024, 7);

        DropPositionsDto? positions = session.Counters.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal(0, positions.AnchorPcr);
        Assert.Equal([new DropBucketDto(5, 3, 0)], positions.Buckets);
        Assert.Empty(positions.Reanchors);
    }

    [Fact]
    public void PacketsLeftScrambledAreCountedInTheSamePassAndPlacedTheSameWay()
    {
        byte[] stream = Broadcast(300, scramblesFrom: 200);

        using TunerSession session = Ran(stream, 100, 251, 37, 1024, 7);

        DropPositionsDto? positions = session.Counters.Snapshot().Positions;

        Assert.Equal(100, session.Counters.ScrambledPackets);
        Assert.Equal(0, session.Counters.Drops);
        Assert.NotNull(positions);
        Assert.Equal(
            100,
            positions.Buckets.Sum(bucket => bucket.Scrambled)
        );
        Assert.Equal([20, 21, 22, 23, 24, 25, 26, 27, 28, 29], positions.Buckets.Select(bucket => bucket.Second));
        Assert.Equal(0, positions.Buckets.Sum(bucket => bucket.Continuity));
    }

    [Fact]
    public void WhatTheSessionCountedIsWhatTheAppIsToldWhileItIsStillRunning()
    {
        byte[] stream = Broadcast(300, losesAfter: 50, losing: 3);

        using TunerSession session = Ran(stream, 100, 251, 37, 1024, 7);

        SessionSnapshot snapshot = SessionViews.Of(session, Hello);
        RecordingSessionDto recording = RecordingSessionDto.Of(Hello, snapshot);

        Assert.True(recording.CcMeasured);
        Assert.True(recording.ScrambleMeasured);
        Assert.Equal(3, recording.CcDropped);
        Assert.Equal(300, recording.CcTotal);
        Assert.Equal(0, recording.ScrambledPackets);
        Assert.NotNull(recording.Positions);
        Assert.Equal([new DropBucketDto(5, 3, 0)], recording.Positions.Buckets);
    }

    [Fact]
    public void ARecordingOfAStreamThatNeverSaysTheTimeIsCountedWithoutBeingPlaced()
    {
        var stream = new List<byte>();
        for (int index = 0; index < 300; index++)
        {
            stream.AddRange(Packet(VideoPid, index % 16));
        }

        using TunerSession session = Ran([.. stream], 100, 251, 37, 1024, 7);

        Assert.True(session.Counters.CcMeasured);
        Assert.Equal(300, session.Counters.Packets);
        Assert.Null(session.Counters.Snapshot().Positions);

        SessionSnapshot snapshot = SessionViews.Of(session, Hello);

        Assert.Null(RecordingSessionDto.Of(Hello, snapshot).Positions);
    }

    private sealed class RememberingRecordingWriter : IRecordingWriter
    {
        private readonly List<byte> written = [];

        public string Path => "/dev/null";

        public long BytesWritten => written.Count;

        public IReadOnlyList<byte> Written => written;

        public void Write(ReadOnlySpan<byte> bytes) => written.AddRange(bytes);

        public void Dispose() { }
    }

    private sealed class ScriptedBytesDevice(IReadOnlyList<byte[]> chunks) : ITunerDevice
    {
        private readonly SemaphoreSlim drained = new(0);

        private int next;

        public long Overflows => 0;

        public bool Disposed { get; private set; }

        public byte[] Read(int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (next < chunks.Count)
            {
                return chunks[next++];
            }

            drained.Release();
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();

            return [];
        }

        public void AwaitDrained(TimeSpan within) =>
            Assert.True(
                drained.Wait(within),
                "The session never read the whole of the stream it was given."
            );

        public void Dispose() => Disposed = true;
    }
}
