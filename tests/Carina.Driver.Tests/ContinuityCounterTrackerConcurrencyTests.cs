using Carina.Contracts;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class ContinuityCounterTrackerConcurrencyTests
{
    private const int VideoPid = 0x0100;
    private const long Second = 90_000;
    private const int Readings = 200;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact]
    public void WhatWasPlacedNeverOutRunsWhatWasCountedWhileTheStreamIsStillArriving()
    {
        var tracker = new ContinuityCounterTracker();
        using var counting = new CancellationTokenSource();

        Thread reader = Count(tracker, counting.Token);
        var torn = new List<string>();
        long before = tracker.Snapshot().Drops;
        long after = before;
        int located = 0;

        try
        {
            DateTime deadline = DateTime.UtcNow + Patience;

            while (Short(located, after - before) && DateTime.UtcNow < deadline)
            {
                SessionCounters counters = tracker.Snapshot();

                if (counters.Positions is not { } positions)
                {
                    continue;
                }

                located++;
                after = counters.Drops;

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
            reader.Join(Patience);
        }

        Assert.True(
            located >= Readings,
            $"only {located} reads saw a position, so this proves little about reading one."
        );
        Assert.True(
            after - before >= Readings,
            $"the stream added only {after - before} losses while {located} reads went by, so the reads raced nothing."
        );
        Assert.Empty(torn);
    }

    private static bool Short(int located, long counted) =>
        located < Readings || counted < Readings;

    private static Thread Count(ContinuityCounterTracker tracker, CancellationToken stopping)
    {
        var thread = new Thread(() =>
        {
            int counter = 0;
            long clock = 0;

            while (!stopping.IsCancellationRequested)
            {
                if (counter % 50 is 0)
                {
                    clock += Second / 10;
                }

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
