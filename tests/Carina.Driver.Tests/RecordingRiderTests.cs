using Carina.Contracts;
using Carina.Driver.Configuration;
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

    [Fact]
    public void ARecordingRiderThatWasCutOffWhileItsHostRanOnFailsAsARecording()
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
        using var device = new PiggybackTunerDevice(host, seat);

        host.Broadcaster.Unsubscribe(seat, new TimeoutException("the recording was too slow"));

        StreamCutException cut = Assert.Throws<StreamCutException>(
            () => device.Read(1, CancellationToken.None)
        );

        Assert.Equal(SessionStopReason.RecordingFailed, cut.Reason);

        host.Dispose();
    }

    [Fact]
    public async Task ARiderWhoseHostSimplyEndedCarriesTheHostsOwnReason()
    {
        var device = new PacedTunerDevice();
        var host = new TunerSession(
            SessionId.Parse("host"),
            SessionPurpose.Live,
            "adapter0",
            device,
            Start,
            Start.AddHours(1),
            clock
        );

        SessionSubscription seat = host.Broadcaster.Subscribe(SubscriberKind.Recording);
        using var rider = new PiggybackTunerDevice(host, seat);

        host.Start();
        device.AwaitParkedBefore(1);
        host.Stop(SessionStopReason.Preempted);

        await host.Completion.WaitAsync(Deadlock);

        Assert.True(host.Concluded);
        Assert.Equal(SessionStopReason.Preempted, host.StopReason);

        StreamCutException cut = Assert.Throws<StreamCutException>(
            () => rider.Read(1, CancellationToken.None)
        );

        Assert.Equal(SessionStopReason.Preempted, cut.Reason);
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
