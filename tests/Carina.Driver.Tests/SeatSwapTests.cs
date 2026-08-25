using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class MarkedTunerDevice : ITunerDevice
{
    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim allowed = new(0);
    private readonly SemaphoreSlim parked = new(0);

    private long reads;
    private long parks;
    private int seen;

    public long Reads => Interlocked.Read(ref reads);

    public long Parks => Interlocked.Read(ref parks);

    public long Overflows { get; set; }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref parks);
        parked.Release();
        allowed.Wait(cancellationToken);

        long taken = Interlocked.Increment(ref reads);
        byte[] packet = new byte[TsPacketReader.PacketLength];

        packet[0] = 0x47;
        packet[1] = 0x01;
        packet[2] = 0x00;
        packet[3] = (byte)(0x10 | (taken % 16));

        for (int offset = 4; offset < packet.Length; offset++)
        {
            packet[offset] = (byte)taken;
        }

        return packet;
    }

    public static int MarkOf(byte[] packet) => packet[4];

    public void Allow(int chunks) => allowed.Release(chunks);

    public void AwaitParkedBefore(int read)
    {
        while (seen < read)
        {
            Assert.True(
                parked.Wait(Deadlock),
                $"Nothing settled before read {seen + 1}; the tuner is being read by nobody."
            );

            seen++;
        }
    }

    public void Dispose() { }
}

public sealed class SeatedTunerDevice(SessionSubscription seat) : ITunerDevice
{
    public long Overflows => 0;

    public byte[] Read(int count, CancellationToken cancellationToken) =>
        seat.Reader.ReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();

    public void Dispose() { }
}

public sealed class RecallingRecordingWriter(string path) : IRecordingWriter
{
    private readonly SemaphoreSlim written = new(0);
    private readonly Lock gate = new();
    private readonly List<int> marks = [];

    private long bytesWritten;

    public string Path { get; } = path;

    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    public IReadOnlyList<int> Marks
    {
        get
        {
            lock (gate)
            {
                return [.. marks];
            }
        }
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            marks.Add(bytes[4]);
        }

        Interlocked.Add(ref bytesWritten, bytes.Length);
        written.Release();
    }

    public void AwaitChunks(int count, TimeSpan within)
    {
        for (int taken = 0; taken < count; taken++)
        {
            Assert.True(
                written.Wait(within),
                $"The recording was handed {taken} chunks and never the {count} it was waiting for."
            );
        }
    }

    public void Dispose() { }
}

public sealed class RecallingRecordingWriterFactory : IRecordingWriterFactory
{
    public RecallingRecordingWriter? Last { get; private set; }

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Last = new RecallingRecordingWriter(
            System.IO.Path.Combine(recordingsDirectory, $"{recordingId}.ts")
        );

        return Last;
    }
}

public sealed class SeatSwapTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan NoPatience = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan FarLongerThanThisTestWaits = TimeSpan.FromMinutes(5);

    private readonly string root = Directory.CreateTempSubdirectory("carina-seat-").FullName;
    private readonly ManualTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Terrestrial),
            ]
        );

    private TunerSessionManager Manager(
        ITunerDeviceFactory? devices = null,
        IRecordingWriterFactory? writers = null,
        TimeSpan? handOverLimit = null
    ) =>
        new(
            Configuration,
            devices ?? new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: writers,
            handOverLimit: handOverLimit
        );

    private static StartSessionRequest Request(string sessionId, SessionPurpose purpose) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = purpose,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            OutputRoot = purpose is SessionPurpose.Recording ? "primary" : null,
            RecordingId = purpose is SessionPurpose.Recording ? $"k-{sessionId}" : null,
            EndsAt = Start.AddHours(1),
        };

    private static TunerSession Started(TunerSessionManager manager, StartSessionRequest request)
    {
        SessionStart start = manager.Begin(request);

        Assert.True(start.TryGetSession(out TunerSession? session), start.Detail);

        return session;
    }

    private static void StopAll(params TunerSession[] sessions)
    {
        foreach (TunerSession session in sessions)
        {
            session.Stop();
        }

        foreach (TunerSession session in sessions)
        {
            session.WaitForEnd(Deadlock);
        }
    }

    private TunerSession Watching(ITunerDevice device) =>
        new(
            SessionId.Parse("watching"),
            SessionPurpose.Live,
            "adapter0",
            device,
            Start,
            Start.AddHours(1),
            clock,
            chunkSize: TsPacketReader.PacketLength
        );

    [Fact]
    public async Task ASessionToldToReadElsewhereLeavesTheTunerAloneFromThereOn()
    {
        var device = new MarkedTunerDevice();
        using TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster();

        SessionSubscription viewer = watching.Broadcaster.Subscribe(SubscriberKind.Viewer);

        watching.Start();
        device.AwaitParkedBefore(1);
        device.Allow(1);
        device.AwaitParkedBefore(2);

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);
        Task<bool> handOver = Task.Run(() =>
            watching.ReadFromInstead(new SeatedTunerDevice(seat), null, seat, Deadlock)
        );

        Assert.NotSame(
            handOver,
            await Task.WhenAny(handOver, Task.Delay(NoPatience))
        );
        Assert.Equal(1, device.Reads);

        device.Allow(1);

        Assert.True(await handOver);

        seats.Publish(Marked(9));

        Assert.Equal([1, 2, 9], Taken(viewer, 3));
        Assert.Equal(2, device.Reads);
    }

    [Fact]
    public void ASessionThatWillNotComeOutOfItsReadKeepsTheTuner()
    {
        var device = new HeldOpenTunerDevice();
        using TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster();

        watching.Start();

        Assert.True(device.Reading.Wait(Deadlock));

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);

        Assert.False(
            watching.ReadFromInstead(new SeatedTunerDevice(seat), null, seat, NoPatience)
        );
        Assert.Equal(SessionState.Active, watching.State);
        Assert.Null(watching.RidesOn);

        device.LetGo();

        Assert.True(watching.ReadFromInstead(new SeatedTunerDevice(seat), null, seat, Deadlock));
    }

    [Fact]
    public async Task ASessionThatHasEndedTurnsANewStreamDownRatherThanSittingOnIt()
    {
        var device = new ScriptedTunerDevice();
        TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster();

        watching.Start();
        watching.Stop();
        watching.WaitForEnd(Deadlock);

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);
        Task<bool> handOver = Task.Run(() =>
            watching.ReadFromInstead(
                new SeatedTunerDevice(seat),
                null,
                seat,
                FarLongerThanThisTestWaits
            )
        );

        Assert.Same(handOver, await Task.WhenAny(handOver, Task.Delay(Deadlock)));
        Assert.False(await handOver);

        watching.Dispose();
    }

    [Fact]
    public async Task TheHandOverIsAnsweredWhenTheReaderMovesAndNotWhenPatienceRunsOut()
    {
        var device = new ScriptedTunerDevice();
        using TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster();

        watching.Start();

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);
        Task<bool> handOver = Task.Run(() =>
            watching.ReadFromInstead(
                new SeatedTunerDevice(seat),
                null,
                seat,
                FarLongerThanThisTestWaits
            )
        );

        Assert.Same(handOver, await Task.WhenAny(handOver, Task.Delay(Deadlock)));
        Assert.True(await handOver);
    }

    [Fact]
    public void WhatTheNewSeatDropsIsWhatTheSessionReports()
    {
        var device = new ScriptedTunerDevice();
        using TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster(viewerCapacity: 1);

        watching.Start();

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);

        seats.Publish(Marked(1));
        seats.Publish(Marked(2));
        seats.Publish(Marked(3));

        Assert.Equal(2, seat.DroppedChunks);
        Assert.Equal(0, watching.DroppedChunks);

        Assert.True(watching.ReadFromInstead(new SeatedTunerDevice(seat), null, seat, Deadlock));

        Assert.Equal(2, watching.DroppedChunks);
    }

    [Fact]
    public void TheOverflowsASessionHasAlreadySeenSurviveTheChangeOfSeat()
    {
        var device = new ScriptedTunerDevice { Overflows = 4 };
        using TunerSession watching = Watching(device);
        using var seats = new SessionBroadcaster();

        watching.Start();
        device.Overflows = 7;

        Assert.Equal(3, watching.DeviceOverflows);

        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);

        Assert.True(watching.ReadFromInstead(new SeatedTunerDevice(seat), null, seat, Deadlock));

        Assert.Equal(3, watching.DeviceOverflows);
    }

    [Fact]
    public void ARecordingArrivingOnAWatchedChannelTakesTheTunerAndTheWatcherRidesOnIt()
    {
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));
        TunerSession recording = Started(manager, Request("s-2", SessionPurpose.Recording));

        Assert.Equal(watching.DeviceId, recording.DeviceId);
        Assert.Same(recording, watching.RidesOn);
        Assert.Null(recording.RidesOn);
        Assert.Equal([SubscriberKind.Piggyback], recording.Broadcaster.KindsInUse);
        Assert.Empty(watching.Broadcaster.KindsInUse);

        StopAll(recording, watching);
    }

    [Fact]
    public void TheRecordingThatTookTheTunerKeepsTheWindowItAskedForAndCanBeGivenMore()
    {
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(
            manager,
            Request("s-1", SessionPurpose.Live) with { EndsAt = Start.AddMinutes(30) }
        );
        TunerSession recording = Started(
            manager,
            Request("s-2", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(60) }
        );

        Assert.Equal(Start.AddMinutes(60), recording.EndsAt);

        SessionExtension longer = manager.Extend(
            recording.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(90) }
        );

        Assert.Equal(SessionExtendOutcome.Extended, longer.Outcome);
        Assert.Equal(Start.AddMinutes(90), recording.EndsAt);
        Assert.Equal(
            Start.AddMinutes(90),
            SessionViews.Of(recording, new DriverHello(DriverProtocol.Version, "instance", [])).EndsAt
        );

        StopAll(recording, watching);
    }

    [Fact]
    public void TheWatcherHandedDownIsNotToldItsSessionEnded()
    {
        var hello = new DriverHello(DriverProtocol.Version, "instance", []);
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));

        SessionSubscription viewer = watching.Broadcaster.Subscribe(SubscriberKind.Viewer);

        TunerSession recording = Started(manager, Request("s-2", SessionPurpose.Recording));

        Assert.Same(recording, watching.RidesOn);
        Assert.Equal(SessionState.Active, watching.State);
        Assert.Equal(SessionStopReason.Running, watching.StopReason);
        Assert.False(watching.Concluded);
        Assert.Null(watching.FailureCause);
        Assert.False(viewer.IsDisconnected);
        Assert.Equal(1, watching.Broadcaster.SubscriberCount);

        SessionSnapshot snapshot = SessionViews.Of(watching, hello);

        Assert.Equal(SessionId.Parse("s-1"), snapshot.SessionId);
        Assert.Equal(SessionState.Active, snapshot.State);
        Assert.Equal("adapter0", snapshot.DeviceId);
        Assert.False(snapshot.Concluded);
        Assert.Contains(
            SessionViews.All(manager, hello),
            listed => listed.SessionId == watching.SessionId && listed.State is SessionState.Active
        );

        StopAll(recording, watching);
    }

    [Fact]
    public async Task NoByteOfTheStreamIsReadTwiceOrLostWhenTheSeatChangesHands()
    {
        var device = new MarkedTunerDevice();
        var writers = new RecallingRecordingWriterFactory();
        TunerSessionManager manager = Manager(new OneTunerDeviceFactory(device), writers);

        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));
        SessionSubscription viewer = watching.Broadcaster.Subscribe(SubscriberKind.Viewer);

        device.AwaitParkedBefore(1);
        device.Allow(2);
        device.AwaitParkedBefore(3);

        Task<SessionStart> begun = Task.Run(() =>
            manager.Begin(Request("s-2", SessionPurpose.Recording))
        );

        Assert.NotSame(
            begun,
            await Task.WhenAny(begun, Task.Delay(NoPatience))
        );
        Assert.Equal(2, device.Reads);
        Assert.Equal(3, device.Parks);

        while (!begun.IsCompleted)
        {
            device.Allow(1);

            await Task.WhenAny(begun, Task.Delay(NoPatience));
        }

        SessionStart start = await begun;

        Assert.True(start.TryGetSession(out TunerSession? recording), start.Detail);
        Assert.Same(recording, watching.RidesOn);

        int lastTheWatcherTook = (int)device.Reads;

        device.Allow(2);

        Assert.NotNull(writers.Last);
        writers.Last.AwaitChunks(2, Deadlock);

        IReadOnlyList<int> recorded = writers.Last.Marks;
        int[] unbroken = [.. Enumerable.Range(recorded[0], recorded.Count)];
        int[] everythingTheTunerGave = [.. Enumerable.Range(1, recorded[^1])];

        Assert.Equal(lastTheWatcherTook + 1, recorded[0]);
        Assert.Equal(unbroken, recorded);
        Assert.Equal(everythingTheTunerGave, Taken(viewer, recorded[^1]));

        StopAll(recording, watching);
    }

    [Fact]
    public void AnotherRecordingArrivingLaterRidesOnTheOneHoldingTheTuner()
    {
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));
        TunerSession recording = Started(manager, Request("s-2", SessionPurpose.Recording));
        TunerSession second = Started(manager, Request("s-3", SessionPurpose.Recording));

        Assert.Same(recording, second.RidesOn);
        Assert.Same(recording, watching.RidesOn);
        Assert.Null(recording.RidesOn);
        Assert.Equal(
            [SubscriberKind.Piggyback, SubscriberKind.Recording],
            recording.Broadcaster.KindsInUse.OrderBy(kind => kind).ToArray()
        );

        StopAll(second, recording, watching);
    }

    [Fact]
    public async Task TheDrainStopsTheWatcherAndLeavesTheRecordingRunning()
    {
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));
        TunerSession recording = Started(manager, Request("s-2", SessionPurpose.Recording));

        Task draining = manager.DrainAsync(CancellationToken.None);

        watching.WaitForEnd(Deadlock);

        Assert.Equal(SessionState.Stopped, watching.State);
        Assert.Equal(SessionState.Active, recording.State);
        Assert.False(draining.IsCompleted);

        clock.Advance(TimeSpan.FromHours(2));

        await draining.WaitAsync(Deadlock);

        Assert.Equal(SessionState.Stopped, recording.State);
        Assert.Equal(SessionStopReason.EndTimeReached, recording.StopReason);
    }

    [Fact]
    public void TheRecordingIsRefusedWhenTheWatcherWillNotComeOutOfItsRead()
    {
        var device = new HeldOpenTunerDevice();
        TunerSessionManager manager = Manager(
            new OneTunerDeviceFactory(device),
            handOverLimit: NoPatience
        );

        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));

        Assert.True(device.Reading.Wait(Deadlock));

        SessionStart refused = manager.Begin(Request("s-2", SessionPurpose.Recording));

        Assert.Equal(SessionRefusal.DeviceBusy, refused.Refusal);
        Assert.Contains("s-1", refused.Detail, StringComparison.Ordinal);
        Assert.Null(watching.RidesOn);
        Assert.Equal(SessionState.Active, watching.State);
        Assert.False(manager.TryGet(SessionId.Parse("s-2"), out _));
        Assert.Single(manager.Sessions);

        SessionStart again = manager.Begin(Request("s-2", SessionPurpose.Recording));

        Assert.Equal(SessionRefusal.DeviceBusy, again.Refusal);

        device.LetGo();
        StopAll(watching);
    }

    [Fact]
    public void NothingIsTakenFromAnybodyWhileTheDriverIsShuttingDown()
    {
        var device = new HeldOpenTunerDevice();
        TunerSessionManager manager = Manager(new OneTunerDeviceFactory(device));
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));

        manager.EnterDraining();

        SessionStart refused = manager.Begin(Request("s-2", SessionPurpose.Recording));

        Assert.Equal(SessionRefusal.Draining, refused.Refusal);
        Assert.Null(watching.RidesOn);
        Assert.Equal(SessionState.Active, watching.State);

        device.LetGo();
        StopAll(watching);
    }

    [Fact]
    public async Task TwoRecordingsArrivingAtOnceLeaveOneHoldingTheTunerAndOneRidingOnIt()
    {
        TunerSessionManager manager = Manager();
        TunerSession watching = Started(manager, Request("s-1", SessionPurpose.Live));

        using var both = new Barrier(2);

        Task<SessionStart>[] begun = [.. new[] { "s-2", "s-3" }
            .Select(sessionId =>
                Task.Run(() =>
                {
                    both.SignalAndWait();

                    return manager.Begin(Request(sessionId, SessionPurpose.Recording));
                })
            )];

        SessionStart[] started = await Task.WhenAll(begun);

        TunerSession[] running = [.. started.Select(start => start.Session).OfType<TunerSession>()];

        Assert.NotEmpty(running);
        Assert.Equal(1, running.Count(session => session.RidesOn is null));

        TunerSession holder = running.Single(session => session.RidesOn is null);

        Assert.Same(holder, watching.RidesOn);
        Assert.All(running, session => Assert.NotSame(watching, session.RidesOn));
        Assert.Equal(SessionState.Active, watching.State);

        StopAll([.. running, watching]);
    }

    private static byte[] Marked(byte mark)
    {
        byte[] packet = new byte[TsPacketReader.PacketLength];

        packet[0] = 0x47;
        packet[4] = mark;

        return packet;
    }

    private static IReadOnlyList<int> Taken(SessionSubscription seat, int count)
    {
        var marks = new List<int>();

        for (int taken = 0; taken < count; taken++)
        {
            byte[] chunk = seat
                .Reader.ReadAsync()
                .AsTask()
                .WaitAsync(Deadlock)
                .GetAwaiter()
                .GetResult();

            marks.Add(MarkedTunerDevice.MarkOf(chunk));
        }

        return marks;
    }
}
