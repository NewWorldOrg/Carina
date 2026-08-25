using Carina.Contracts;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class ContinuityCounterTrackerConcurrencyTests
{
    private const int VideoPid = 0x0100;
    private const long Second = 90_000;
    private const int Readings = 400;

    [Fact]
    public void WhatWasPlacedNeverOutRunsWhatWasCountedWhileTheStreamIsStillArriving()
    {
        var tracker = new ContinuityCounterTracker();
        using var counting = new CancellationTokenSource();

        Thread reader = Count(tracker, counting.Token);
        var torn = new List<string>();

        try
        {
            for (int read = 0; read < Readings; read++)
            {
                SessionCounters counters = tracker.Snapshot();

                if (counters.Positions is not { } positions)
                {
                    continue;
                }

                long placed = positions.Buckets.Sum(bucket => bucket.Continuity);
                long left = positions.Buckets.Sum(bucket => bucket.Scrambled);

                if (placed > counters.Drops)
                {
                    torn.Add($"{placed} losses placed against {counters.Drops} counted");
                }

                if (left > counters.ScrambledPackets)
                {
                    torn.Add($"{left} scrambled placed against {counters.ScrambledPackets} counted");
                }

                if (!counters.CcMeasured)
                {
                    torn.Add("a position on a stream nothing had counted");
                }
            }
        }
        finally
        {
            counting.Cancel();
            reader.Join(TimeSpan.FromSeconds(30));
        }

        Assert.Empty(torn);
    }

    private static Thread Count(ContinuityCounterTracker tracker, CancellationToken stopping)
    {
        var thread = new Thread(() =>
        {
            int counter = 0;
            long clock = 0;

            while (!stopping.IsCancellationRequested)
            {
                clock += Second / 10;
                tracker.Observe(Packet(counter % 16, clock));
                counter += 2;
                tracker.Observe(Packet(counter % 16, null, scrambled: true));
                counter++;
            }
        })
        {
            IsBackground = true,
            Name = "counting",
        };

        thread.Start();

        return thread;
    }

    private static TsPacket Packet(int counter, long? pcr, bool scrambled = false) =>
        new(
            VideoPid,
            counter,
            HasPayload: true,
            TransportError: false,
            Scrambled: scrambled,
            Discontinuity: false,
            PayloadUnitStart: false,
            PayloadHash: counter + 1,
            Provisional: false,
            Pcr: pcr
        );
}
