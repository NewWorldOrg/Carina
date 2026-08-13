using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class TunerSessionTests : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("carina-session-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private TunerSession Session(
        ScriptedTunerDevice device,
        ManualTimeProvider clock,
        RecordingWriter? writer = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TimeSpan? runsFor = null
    ) =>
        new(
            SessionId.Parse("s-1"),
            purpose,
            "adapter0",
            device,
            Start,
            Start + (runsFor ?? TimeSpan.FromHours(1)),
            clock,
            writer,
            TsPacketReader.PacketLength * 4
        );

    private RecordingWriter Writer(string name = "s-1") =>
        new(root, SessionId.Parse(name));

    private static void WaitForEnd(TunerSession session) =>
        session.WaitForEnd(TimeSpan.FromSeconds(10));

    [Fact]
    public void ASessionEndsItselfAtItsEndTimeWithNoAppConnected()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        clock.Advance(TimeSpan.FromHours(2));
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public void ADeliberateStopEndsTheSessionAsStopped()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
    }

    [Fact]
    public void ADeviceFailureEndsTheSessionAsFailedAndNotStopped()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(failAfterReads: 3), clock, Writer());

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.IsType<IOException>(session.FailureCause);
    }

    [Fact]
    public void AWriteFailureEndsTheSessionAsFailedAndNotStopped()
    {
        var clock = new ManualTimeProvider(Start);
        var writer = Writer();
        writer.Dispose();

        using var session = Session(new ScriptedTunerDevice(), clock, writer);

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
    }

    [Fact]
    public void TheBytesThatWereReadAreTheBytesThatWereWritten()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        var written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.Equal(device.Reads * TsPacketReader.PacketLength * 4L, written);
        Assert.Equal(written, session.BytesRecorded);
    }

    [Fact]
    public void TheDeviceIsReadOncePerChunk()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        var seen = session.Counters.Packets + session.Counters.ProvisionalPackets;

        Assert.True(device.Reads > 0);
        Assert.InRange(seen, (device.Reads - 1) * 4L, device.Reads * 4L);
    }

    [Fact]
    public void AStalledViewerDoesNotCostTheRecordingAByte()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        var stalled = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        Thread.Sleep(50);
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(device.Reads * TsPacketReader.PacketLength * 4L, session.BytesRecorded);
        Assert.True(stalled.DroppedChunks > 0);
        Assert.False(stalled.IsDisconnected);
    }

    [Fact]
    public void AStalledSurveyReaderIsDisconnectedRatherThanSlowingTheRecording()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        var stalled = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        Thread.Sleep(50);
        session.Stop();
        WaitForEnd(session);

        Assert.True(stalled.IsDisconnected);
        Assert.Equal(device.Reads * TsPacketReader.PacketLength * 4L, session.BytesRecorded);
    }

    [Fact]
    public void ASubscriberComingAndGoingDoesNotChangeTheSessionState()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        var subscription = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        Assert.Equal(SessionState.Active, session.State);

        session.Broadcaster.Unsubscribe(subscription);

        Assert.Equal(SessionState.Active, session.State);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void MeasurementCarriesOnAcrossReadsWithinOneSession()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        Thread.Sleep(50);
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(0, session.Counters.Drops);
        Assert.True(session.Counters.Packets > 4);
    }

    [Fact]
    public void AnEndTimeMovesForwardAndNeverBack()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        Assert.True(session.Extend(Start.AddHours(3)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);

        Assert.False(session.Extend(Start.AddHours(2)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);
    }

    [Fact]
    public void ASessionCannotEndBeforeItBegins()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new TunerSession(
                    SessionId.Parse("s-2"),
                    SessionPurpose.Live,
                    "adapter0",
                    new ScriptedTunerDevice(),
                    Start,
                    Start,
                    new ManualTimeProvider(Start)
                )
        );
    }

    [Fact]
    public void ASessionStartsOnce()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();

        Assert.Throws<InvalidOperationException>(session.Start);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void TheDeviceIsReleasedWhenTheSessionEnds()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.True(device.Disposed);
    }
}
