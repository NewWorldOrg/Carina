using Carina.Contracts;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class ContinuityCounterTrackerConcurrencyTests
{
    private const int VideoPid = 0x0100;
    private const int PacketLength = 188;
    private const int PacketsPerChunk = 8;
    private const int Reads = 200_000;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static readonly DriverHello Hello =
        new(DriverProtocol.Version, "instance-1", DriverGreeting.Capabilities);

    [Fact]
    public void WhatTheAppIsToldNeverPlacesMoreThanItSaysWasCounted()
    {
        var device = new EndlessDroppingDevice();
        using TunerSession session = Watching(device);

        session.Start();
        device.AwaitFirstRead(Patience);

        var torn = new List<string>();
        long first = 0;
        long last = 0;
        int located = 0;
        int breaks = 0;

        try
        {
            for (int read = 0; read < Reads; read++)
            {
                SessionCounters counters = SessionViews.Of(session, Hello).Counters;

                if (counters.Positions is not { } positions)
                {
                    continue;
                }

                located++;
                last = counters.Drops;
                if (first is 0)
                {
                    first = counters.Drops;
                }

                long placed = positions.Buckets.Sum(bucket => bucket.Continuity);
                long left = positions.Buckets.Sum(bucket => bucket.Scrambled);

                if (placed > counters.Drops)
                {
                    torn.Add($"read {read}: {placed} losses placed against {counters.Drops} counted");
                }

                if (left > counters.ScrambledPackets)
                {
                    torn.Add($"read {read}: {left} scrambled placed against {counters.ScrambledPackets} counted");
                }

                if (!counters.CcMeasured)
                {
                    torn.Add($"read {read}: a position on a stream nothing had counted");
                }

                if (positions.Reanchors.Select(reanchor => reanchor.Second).Order()
                    is var seconds && !seconds.SequenceEqual(
                        positions.Reanchors.Select(reanchor => reanchor.Second)))
                {
                    torn.Add($"read {read}: the breaks in the clock did not read forwards");
                }

                if (positions.Reanchors.Any(reanchor =>
                    reanchor.Before < 0
                    || reanchor.Before >= PcrTimeline.WrapsAt
                    || reanchor.After < 0
                    || reanchor.After >= PcrTimeline.WrapsAt))
                {
                    torn.Add($"read {read}: a break named a clock reading outside the standard");
                }

                breaks = Math.Max(breaks, positions.Reanchors.Count);
            }
        }
        finally
        {
            session.Stop();
            session.WaitForEnd(Patience);
        }

        Assert.True(
            located > Reads / 2,
            $"only {located} of {Reads} reads saw a position, so most of them measured nothing."
        );
        Assert.True(
            last - first >= Reads / 10,
            $"the stream added only {last - first} losses across {located} reads, so the reads raced nothing."
        );
        Assert.True(breaks > 0, "the clock never broke, so the re-anchors were never read.");
        Assert.Empty(torn);
    }

    private static TunerSession Watching(ITunerDevice device) =>
        new(
            SessionId.Parse("torn-1"),
            SessionPurpose.Live,
            "adapter0",
            device,
            Start,
            Start + TimeSpan.FromHours(1),
            new ManualTimeProvider(Start),
            chunkSize: PacketLength * PacketsPerChunk
        );

    private static byte[] Packet(
        int counter,
        long? pcr = null,
        bool scrambled = false,
        bool breaking = false
    )
    {
        byte[] packet = new byte[PacketLength];
        Array.Fill(packet, (byte)(counter + 1), 4, PacketLength - 4);

        packet[0] = TsPacketReader.SyncByte;
        packet[1] = (byte)((VideoPid >> 8) & 0x1F);
        packet[2] = (byte)(VideoPid & 0xFF);
        packet[3] = (byte)(
            (scrambled ? 0xC0 : 0x00) | (pcr is null ? 0x10 : 0x30) | (counter & 0x0F)
        );

        if (pcr is not { } reference)
        {
            return packet;
        }

        packet[4] = 7;
        packet[5] = (byte)(breaking ? 0x90 : 0x10);
        packet[6] = (byte)(reference >> 25);
        packet[7] = (byte)((reference >> 17) & 0xFF);
        packet[8] = (byte)((reference >> 9) & 0xFF);
        packet[9] = (byte)((reference >> 1) & 0xFF);
        packet[10] = (byte)(((int)(reference & 1) << 7) | 0x7E);

        return packet;
    }

    private sealed class EndlessDroppingDevice : ITunerDevice
    {
        private static readonly byte[] Opening = Build(withClock: true);
        private static readonly byte[] Rest = Build(withClock: false);

        private readonly SemaphoreSlim reading = new(0);

        private int reads;

        public long Overflows => 0;

        public bool Disposed { get; private set; }

        public byte[] Read(int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reading.Release();

            return Interlocked.Increment(ref reads) is 1 ? Opening : Rest;
        }

        public void AwaitFirstRead(TimeSpan within) =>
            Assert.True(reading.Wait(within), "The session never read a byte from the tuner.");

        public void Dispose() => Disposed = true;

        private static byte[] Build(bool withClock)
        {
            var chunk = new List<byte>();

            for (int index = 0; index < PacketsPerChunk; index++)
            {
                chunk.AddRange(
                    Packet(
                        (index * 2) % 16,
                        index is 0 ? (withClock ? 4_500_000 : 4_500_001) : null,
                        scrambled: index is PacketsPerChunk - 1,
                        breaking: !withClock && index is 0));
            }

            return [.. chunk];
        }
    }
}
