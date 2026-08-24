using System.Diagnostics;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class RecordingRiderTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan NoPatience = TimeSpan.FromMilliseconds(200);

    private readonly string root = Directory.CreateTempSubdirectory("carina-rider-").FullName;
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

    private TunerSessionManager Manager() =>
        new(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance
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

    private static byte[] Chunk(byte seed) => [seed, seed, seed];

    [Fact]
    public void AViewerThatFallsBehindLosesTheOldestChunkItHadNotTakenYet()
    {
        using var broadcaster = new SessionBroadcaster(viewerCapacity: 2);
        SessionSubscription viewer = broadcaster.Subscribe(SubscriberKind.Viewer);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));
        broadcaster.Publish(Chunk(3));

        Assert.Equal(1, viewer.DroppedChunks);
        Assert.True(viewer.Reader.TryRead(out byte[]? first));
        Assert.Equal(Chunk(2), first);
    }

    [Fact]
    public async Task ARecordingThatFallsBehindHoldsThePublisherUpInsteadOfLosingAChunk()
    {
        using var broadcaster = new SessionBroadcaster(
            recordingCapacity: 2,
            recordingBlockLimit: Deadlock
        );
        SessionSubscription rider = broadcaster.Subscribe(SubscriberKind.Recording);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));

        Task third = Task.Run(() => broadcaster.Publish(Chunk(3)));

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.False(
            third.IsCompleted,
            "The publisher carried on with a full recording channel, so a chunk went nowhere."
        );

        Assert.True(rider.Reader.TryRead(out byte[]? first));

        await third.WaitAsync(Deadlock);

        Assert.Equal(Chunk(1), first);
        Assert.True(rider.Reader.TryRead(out byte[]? second));
        Assert.Equal(Chunk(2), second);
        Assert.True(rider.Reader.TryRead(out byte[]? held));
        Assert.Equal(Chunk(3), held);
        Assert.Equal(0, rider.DroppedChunks);
        Assert.False(rider.IsTruncated);
    }

    [Fact]
    public void ARecordingThatNeverCatchesUpIsCutOffRatherThanQuietlyMissingBytes()
    {
        using var broadcaster = new SessionBroadcaster(
            recordingCapacity: 1,
            recordingBlockLimit: NoPatience
        );
        SessionSubscription rider = broadcaster.Subscribe(SubscriberKind.Recording);

        broadcaster.Publish(Chunk(1));
        broadcaster.Publish(Chunk(2));

        Assert.True(rider.IsTruncated);
        Assert.True(rider.IsDisconnected);
        Assert.Equal(1, rider.DroppedChunks);
        Assert.Equal(0, broadcaster.SubscriberCount);
    }

    [Fact]
    public void ARecordingRidingOnAnotherSessionTakesTheSeatThatWaits()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(manager, Request("s-1", SessionPurpose.Live));
        TunerSession rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        Assert.Equal(host.DeviceId, rider.DeviceId);
        Assert.Equal([SubscriberKind.Recording], host.Broadcaster.KindsInUse);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Fact]
    public void AViewerRidingOnAnotherSessionStillTakesTheSeatThatDrops()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(manager, Request("s-1", SessionPurpose.Live));
        TunerSession rider = Started(manager, Request("s-2", SessionPurpose.Live));

        Assert.Equal(host.DeviceId, rider.DeviceId);
        Assert.Equal([SubscriberKind.Piggyback], host.Broadcaster.KindsInUse);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Theory]
    [InlineData(SessionStopReason.EndTimeReached)]
    [InlineData(SessionStopReason.Requested)]
    [InlineData(SessionStopReason.Preempted)]
    [InlineData(SessionStopReason.DrainCapReached)]
    public void ARiderReadsTheReasonOffItsSeatWhileTheHostIsStillPuttingItselfAway(
        SessionStopReason ending
    )
    {
        var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock
        );

        SessionSubscription seat = host.Broadcaster.Subscribe(SubscriberKind.Recording);
        using var rider = new PiggybackTunerDevice(host, seat);

        host.Broadcaster.Close(null, ending);

        Assert.False(
            host.Concluded,
            "The host had already finished, so this is not the window a rider actually wakes in."
        );
        Assert.Equal(SessionStopReason.Running, host.StopReason);

        StreamCutException cut = Assert.Throws<StreamCutException>(
            () => rider.Read(1, CancellationToken.None)
        );

        Assert.Equal(ending, cut.Reason);

        host.Dispose();
    }

    [Theory]
    [InlineData(SessionStopReason.EndTimeReached)]
    [InlineData(SessionStopReason.Requested)]
    [InlineData(SessionStopReason.Preempted)]
    public async Task ARecordingRidingAlongEndsWithTheReasonItsHostEndedFor(
        SessionStopReason ending
    )
    {
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession host = Started(manager, Request("s-1", SessionPurpose.Live));

        device.AwaitParkedBefore(1);

        TunerSession rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        Assert.Equal(host.DeviceId, rider.DeviceId);

        switch (ending)
        {
            case SessionStopReason.Requested:
                await manager.StopAsync(
                    host.SessionId,
                    "the operator said so",
                    CancellationToken.None
                );

                break;

            case SessionStopReason.Preempted:
                host.Preempt("something more important wanted the tuner");

                break;

            default:
                host.Stop(ending);

                break;
        }

        await rider.Completion.WaitAsync(Deadlock);

        Assert.Equal(SessionState.Failed, rider.State);
        Assert.Equal(ending, rider.StopReason);

        host.Dispose();
    }

    [Fact]
    public async Task ARecordingRidingAlongThroughAShutdownSaysItWasAskedToStop()
    {
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession host = Started(manager, Request("s-1", SessionPurpose.Live));

        device.AwaitParkedBefore(1);

        TunerSession rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        manager.DetachEverySubscriber();

        await rider.Completion.WaitAsync(Deadlock);

        Assert.Equal(SessionState.Failed, rider.State);
        Assert.Equal(SessionStopReason.Requested, rider.StopReason);

        host.Dispose();
    }

    [Fact]
    public void ARiderCutOffForBeingTooSlowIsTheOneThatFailedAndSaysSo()
    {
        var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock
        );

        using var seats = new SessionBroadcaster(
            recordingCapacity: 1,
            recordingBlockLimit: TimeSpan.Zero
        );
        SessionSubscription seat = seats.Subscribe(SubscriberKind.Recording);
        using var rider = new PiggybackTunerDevice(host, seat);

        seats.Publish(Chunk(1));
        seats.Publish(Chunk(2));

        Assert.Equal(Chunk(1), rider.Read(1, CancellationToken.None));

        StreamCutException cut = Assert.Throws<StreamCutException>(
            () => rider.Read(1, CancellationToken.None)
        );

        Assert.Equal(SessionStopReason.RecordingFailed, cut.Reason);

        host.Dispose();
    }

    [Fact]
    public void ARecordingRidingOnAnotherSessionIsNotHeldOpenPastItsHost()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(
            manager,
            Request("s-1", SessionPurpose.Live) with { EndsAt = Start.AddMinutes(30) }
        );
        TunerSession rider = Started(
            manager,
            Request("s-2", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(10) }
        );

        Assert.Same(host, rider.RidesOn);

        SessionExtension beyond = manager.Extend(
            rider.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(45) }
        );

        Assert.Equal(SessionExtendOutcome.NotAnExtension, beyond.Outcome);
        Assert.Contains(host.SessionId.Value!, beyond.Detail, StringComparison.Ordinal);
        Assert.Equal(Start.AddMinutes(10), rider.EndsAt);

        SessionExtension within = manager.Extend(
            rider.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(30) }
        );

        Assert.Equal(SessionExtendOutcome.Extended, within.Outcome);
        Assert.Equal(Start.AddMinutes(30), rider.EndsAt);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARecordingThatAsksForLongerThanItsHostIsGivenTheHostsWindow()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(
            manager,
            Request("s-1", SessionPurpose.Live) with { EndsAt = Start.AddMinutes(30) }
        );
        TunerSession rider = Started(
            manager,
            Request("s-2", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(60) }
        );

        Assert.Same(host, rider.RidesOn);
        Assert.Equal(Start.AddMinutes(30), rider.EndsAt);
        Assert.Equal(
            Start.AddMinutes(30),
            SessionViews.Of(rider, new DriverHello(DriverProtocol.Version, "instance", [])).EndsAt
        );

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARecordingThatAsksForLessThanItsHostKeepsItsOwnWindow()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(
            manager,
            Request("s-1", SessionPurpose.Live) with { EndsAt = Start.AddMinutes(30) }
        );
        TunerSession rider = Started(
            manager,
            Request("s-2", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(20) }
        );

        Assert.Equal(Start.AddMinutes(20), rider.EndsAt);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARecordingIsNotStartedOnAHostWhoseWindowHasAlreadyRunOut()
    {
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession host = Started(
            manager,
            Request("s-1", SessionPurpose.Live) with { EndsAt = Start.AddMinutes(5) }
        );

        device.AwaitParkedBefore(1);
        clock.Advance(TimeSpan.FromMinutes(10));

        SessionStart refused = manager.Begin(
            Request("s-2", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(20) }
        );

        Assert.Equal(SessionRefusal.DeviceUnavailable, refused.Refusal);
        Assert.Contains(host.SessionId.Value!, refused.Detail, StringComparison.Ordinal);
        Assert.Single(manager.Sessions);

        host.Dispose();
    }

    [Fact]
    public void ARecordingHoldingItsOwnTunerIsHeldToNoOneElsesEnd()
    {
        TunerSessionManager manager = Manager();
        TunerSession alone = Started(
            manager,
            Request("s-1", SessionPurpose.Recording) with { EndsAt = Start.AddMinutes(10) }
        );

        Assert.Null(alone.RidesOn);

        SessionExtension extended = manager.Extend(
            alone.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(45) }
        );

        Assert.Equal(SessionExtendOutcome.Extended, extended.Outcome);
        Assert.Equal(Start.AddMinutes(45), alone.EndsAt);

        alone.Stop();
        alone.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARiderFollowsItsHostOnceTheHostItselfHasBeenGivenLonger()
    {
        TunerSessionManager manager = Manager();
        TunerSession host = Started(
            manager,
            Request("s-1", SessionPurpose.Recording) with
            {
                EndsAt = Start.AddMinutes(30),
                RecordingId = "k-host",
            }
        );
        TunerSession rider = Started(
            manager,
            Request("s-2", SessionPurpose.Recording) with
            {
                EndsAt = Start.AddMinutes(10),
                RecordingId = "k-rider",
            }
        );

        Assert.Same(host, rider.RidesOn);
        Assert.Equal(
            SessionExtendOutcome.NotAnExtension,
            manager.Extend(rider.SessionId, new ExtendSessionRequest { EndsAt = Start.AddMinutes(45) }).Outcome
        );

        Assert.Equal(
            SessionExtendOutcome.Extended,
            manager.Extend(host.SessionId, new ExtendSessionRequest { EndsAt = Start.AddMinutes(50) }).Outcome
        );
        Assert.Equal(
            SessionExtendOutcome.Extended,
            manager.Extend(rider.SessionId, new ExtendSessionRequest { EndsAt = Start.AddMinutes(45) }).Outcome
        );
        Assert.Equal(Start.AddMinutes(45), rider.EndsAt);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Theory]
    [InlineData(TunerSettings.DefaultDemuxBufferBytes)]
    [InlineData(4 * 1024 * 1024)]
    [InlineData(64 * 1024 * 1024)]
    public void TheWaitForARiderFitsInsideTheWindowTheDemuxBufferGivesTheHost(int demuxBufferBytes)
    {
        const long fastestBytesPerSecond = 16_500_000 / 8;

        TimeSpan waited = RecordingBackPressure.WithinTheDemuxWindow(demuxBufferBytes);

        Assert.True(
            waited.TotalSeconds * fastestBytesPerSecond <= demuxBufferBytes,
            $"Waiting {waited} costs the host {waited.TotalSeconds * fastestBytesPerSecond} bytes of a {demuxBufferBytes} byte buffer, so the host overruns before the rider is cut."
        );
        Assert.True(waited > TimeSpan.Zero);
    }

    [Fact]
    public void AHostThatIsItselfRecordingNeverWaitsForARider()
    {
        using var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Recording,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock,
            recordingId: "k-host"
        );

        Assert.Equal(TimeSpan.Zero, host.Broadcaster.RecordingWait);
    }

    [Fact]
    public void AHostThatIsWatchingWaitsForTheWindowItsOwnBufferGives()
    {
        using var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock,
            demuxBufferBytes: 4 * 1024 * 1024
        );

        Assert.Equal(
            RecordingBackPressure.WithinTheDemuxWindow(4 * 1024 * 1024),
            host.Broadcaster.RecordingWait
        );
        Assert.NotEqual(
            RecordingBackPressure.WithinTheDemuxWindow(TunerSettings.DefaultDemuxBufferBytes),
            host.Broadcaster.RecordingWait
        );
    }

    [Fact]
    public void TheBufferTheConfigurationNamesIsTheOneTheWaitIsDrawnFrom()
    {
        var manager = new TunerSessionManager(
            Configuration with
            {
                Tuner = new TunerSettings(TunerBackend.Fake, DemuxBufferBytes: 4 * 1024 * 1024),
            },
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession host = Started(manager, Request("s-1", SessionPurpose.Live));

        Assert.Equal(
            RecordingBackPressure.WithinTheDemuxWindow(4 * 1024 * 1024),
            host.Broadcaster.RecordingWait
        );

        host.Stop();
        host.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARiderOnARecordingHostIsCutOffAtOnceRatherThanHeldOn()
    {
        using var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Recording,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock,
            recordingId: "k-host"
        );

        using var seats = new SessionBroadcaster(
            recordingCapacity: 1,
            recordingBlockLimit: host.Broadcaster.RecordingWait
        );
        SessionSubscription seat = seats.Subscribe(SubscriberKind.Recording);

        seats.Publish(Chunk(1));

        long start = Stopwatch.GetTimestamp();

        seats.Publish(Chunk(2));

        Assert.True(
            Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(2),
            "The host was held up waiting for a rider although it is writing a recording of its own."
        );
        Assert.True(seat.IsDisconnected);
        Assert.Equal(SessionStopReason.RecordingFailed, seat.EndedWith);
    }

    [Fact]
    public void ASessionReportsWhatItsOwnSeatLostAndNotWhatItsReadersLost()
    {
        var hello = new DriverHello(DriverProtocol.Version, "instance", []);

        using var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock
        );

        SessionSubscription watching = host.Broadcaster.Subscribe(SubscriberKind.Viewer);

        for (int published = 0; published <= SessionBroadcaster.DefaultViewerCapacity; published++)
        {
            host.Broadcaster.Publish(Chunk(1));
        }

        Assert.Equal(1, watching.DroppedChunks);
        Assert.Equal(1, host.Broadcaster.DroppedChunks);
        Assert.Equal(0, SessionViews.Of(host, hello).DroppedChunks);

        using var seats = new SessionBroadcaster(viewerCapacity: 1);
        SessionSubscription seat = seats.Subscribe(SubscriberKind.Piggyback);

        seats.Publish(Chunk(1));
        seats.Publish(Chunk(2));

        Assert.Equal(1, seat.DroppedChunks);

        using var rider = new TunerSession(
            SessionId.Parse("rider"),
            SessionPurpose.Live,
            "adapter0",
            new ScriptedTunerDevice(),
            Start,
            Start.AddHours(1),
            clock,
            ridesOn: host,
            seat: seat
        );

        Assert.Equal(1, SessionViews.Of(rider, hello).DroppedChunks);
    }

    [Fact]
    public void TheRecordingSeatIsDeepEnoughToCoverTheWriterBetweenTwoFlushes()
    {
        Assert.True(
            (long)SessionBroadcaster.DefaultRecordingCapacity * TunerSession.DefaultChunkSize
                >= RecordingWriter.FlushInterval,
            "A recording rider cannot survive one flush interval of the writer stalling."
        );
    }
}
