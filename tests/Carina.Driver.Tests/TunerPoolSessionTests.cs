using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class TunerPoolSessionTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-pool-").FullName;

    private readonly ManualTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration(params DeviceSettings[] devices) =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            devices.Length is 0 ? [new DeviceSettings("adapter0", DeviceKind.Terrestrial)] : devices
        );

    private TunerSessionManager Manager(
        ITunerDeviceFactory factory,
        DriverConfiguration? configuration = null,
        TimeSpan? grace = null
    ) =>
        new(
            configuration ?? Configuration(),
            factory,
            clock,
            NullLogger<TunerSessionManager>.Instance,
            tunerGrace: grace
        );

    private StartSessionRequest Request(
        string sessionId,
        SessionPurpose purpose,
        int channel = 55,
        string? deviceId = null
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = purpose,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            DeviceId = deviceId,
            OutputRoot = purpose is SessionPurpose.Recording ? "primary" : null,
            EndsAt = Start.AddHours(1),
        };

    private TunerSession Started(TunerSessionManager manager, StartSessionRequest request)
    {
        var start = manager.Begin(request);

        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.True(start.TryGetSession(out var session));

        return session;
    }

    [Fact]
    public void ASecondConsumerOfTheSameTuningOpensNoSecondTuner()
    {
        var factory = new CountingDeviceFactory();
        var manager = Manager(
            factory,
            Configuration(
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Terrestrial)
            )
        );

        var holder = Started(manager, Request("s-1", SessionPurpose.Live));
        var rider = Started(manager, Request("s-2", SessionPurpose.Live));

        Assert.Equal(["adapter0"], factory.Opened);
        Assert.Equal(holder.DeviceId, rider.DeviceId);
        Assert.Equal(1, holder.Broadcaster.SubscriberCount);

        rider.Stop();
        rider.WaitForEnd(Deadlock);
        holder.Stop();
        holder.WaitForEnd(Deadlock);
    }

    [Fact]
    public void AConsumerOfAnotherTuningOpensATunerOfItsOwn()
    {
        var factory = new CountingDeviceFactory();
        var manager = Manager(
            factory,
            Configuration(
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Terrestrial)
            )
        );

        var first = Started(manager, Request("s-1", SessionPurpose.Live));
        var second = Started(manager, Request("s-2", SessionPurpose.Live, channel: 57));

        Assert.Equal(["adapter0", "adapter1"], factory.Opened.Order());
        Assert.NotEqual(first.DeviceId, second.DeviceId);

        first.Stop();
        first.WaitForEnd(Deadlock);
        second.Stop();
        second.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARiderIsGivenTheSameBytesTheHolderReadAndNoOthers()
    {
        var device = new PacedTunerDevice();
        var manager = Manager(new OneDeviceFactory(device));

        var holder = Started(manager, Request("s-1", SessionPurpose.Recording));
        var rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        device.Allow(4);
        device.AwaitParkedBefore(5);

        holder.Stop();
        holder.WaitForEnd(Deadlock);
        rider.WaitForEnd(Deadlock);

        Assert.Equal(4, device.Reads);
        Assert.Equal(4L * TunerSession.DefaultChunkSize, holder.BytesRecorded);
        Assert.Equal(holder.BytesRecorded, rider.BytesRecorded);
    }

    [Fact]
    public void ARiderIsToldOfTheOverrunsAtTheTunerItsBytesCameFrom()
    {
        var device = new PacedTunerDevice();
        var manager = Manager(new OneDeviceFactory(device));

        var holder = Started(manager, Request("s-1", SessionPurpose.Recording));
        var rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        device.Allow(2);
        device.AwaitParkedBefore(3);
        device.Overflows = 4;

        Assert.Equal(4, holder.DeviceOverflows);
        Assert.Equal(4, rider.DeviceOverflows);

        holder.Stop();
        holder.WaitForEnd(Deadlock);
        rider.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARiderDoesNotAskTheFrontendItDoesNotHold()
    {
        var device = new PacedTunerDevice { Signal = new ScriptedQualitySource() };
        var manager = Manager(new OneDeviceFactory(device));

        var holder = Started(manager, Request("s-1", SessionPurpose.Recording));
        var rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        device.Allow(2);
        device.AwaitParkedBefore(3);

        Assert.Equal(1, device.Signal!.Reads);
        Assert.NotNull(holder.Quality);
        Assert.Null(rider.Quality);

        holder.Stop();
        holder.WaitForEnd(Deadlock);
        rider.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARiderWhoseHolderStoppedIsNeverMistakenForOneThatFinished()
    {
        var device = new PacedTunerDevice();
        var manager = Manager(new OneDeviceFactory(device));

        var holder = Started(manager, Request("s-1", SessionPurpose.Recording));
        var rider = Started(manager, Request("s-2", SessionPurpose.Recording));

        device.Allow(2);
        device.AwaitParkedBefore(3);

        holder.Stop();
        holder.WaitForEnd(Deadlock);
        rider.WaitForEnd(Deadlock);

        Assert.Equal(SessionState.Failed, rider.State);
        Assert.NotNull(rider.FailureCause);
        Assert.Contains("incomplete", rider.FailureCause!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConsumerWhoseTunerWasTakenIsCutOffRatherThanClosedPolitely()
    {
        var manager = Manager(new CountingDeviceFactory());

        var scan = Started(manager, Request("s-1", SessionPurpose.Scan));
        var recording = Started(manager, Request("s-2", SessionPurpose.Recording, channel: 57));

        Assert.Equal(SessionState.Failed, scan.State);
        Assert.Equal(SessionStopReason.Preempted, scan.StopReason);
        Assert.NotEqual(SessionStopReason.Requested, scan.StopReason);
        Assert.True(scan.Concluded);
        Assert.Equal(scan.DeviceId, recording.DeviceId);

        recording.Stop();
        recording.WaitForEnd(Deadlock);
    }

    [Fact]
    public void AConsumerWhoseTunerWasTakenLearnsWhatTookIt()
    {
        var manager = Manager(new CountingDeviceFactory());

        var scan = Started(manager, Request("s-1", SessionPurpose.Scan));
        var recording = Started(manager, Request("s-2", SessionPurpose.Recording, channel: 57));

        var because = scan.FailureCause!.Message;

        Assert.Contains("s-2", because, StringComparison.Ordinal);
        Assert.Contains("recording", because, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adapter0", because, StringComparison.Ordinal);

        recording.Stop();
        recording.WaitForEnd(Deadlock);
    }

    [Fact]
    public async Task AReaderOfADisplacedConsumerIsCutOffAndNotLeftToThinkItSawEverything()
    {
        var manager = Manager(new CountingDeviceFactory());

        var scan = Started(manager, Request("s-1", SessionPurpose.Scan));
        var reader = scan.Broadcaster.Subscribe(SubscriberKind.Viewer);

        var recording = Started(manager, Request("s-2", SessionPurpose.Recording, channel: 57));

        var reading = async () =>
        {
            await foreach (var _ in reader.Reader.ReadAllAsync())
            { }
        };

        var cut = await Record.ExceptionAsync(reading);

        Assert.NotNull(cut);
        Assert.Contains("s-2", cut.Message, StringComparison.Ordinal);

        recording.Stop();
        recording.WaitForEnd(Deadlock);
    }

    [Fact]
    public void EveryRiderOfATunerThatChangesHandsIsCutOffToo()
    {
        var manager = Manager(new CountingDeviceFactory());

        var holder = Started(manager, Request("s-1", SessionPurpose.Scan));
        var rider = Started(manager, Request("s-2", SessionPurpose.Scan));

        Started(manager, Request("s-3", SessionPurpose.Recording, channel: 57));

        Assert.Equal(SessionStopReason.Preempted, holder.StopReason);
        Assert.Equal(SessionStopReason.Preempted, rider.StopReason);
        Assert.Equal(SessionState.Failed, rider.State);
    }

    [Fact]
    public void AnEqualReasonLeavesTheConsumerAlreadyOnTheTunerAlone()
    {
        var manager = Manager(new CountingDeviceFactory());

        var first = Started(manager, Request("s-1", SessionPurpose.Recording));

        var refused = manager.Begin(Request("s-2", SessionPurpose.Recording, channel: 57));

        Assert.Equal(SessionRefusal.NoDeviceFree, refused.Refusal);
        Assert.Equal(SessionState.Active, first.State);
        Assert.Equal(SessionStopReason.Running, first.StopReason);
        Assert.False(first.Concluded);
        Assert.Single(manager.Sessions);

        first.Stop();
        first.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ALesserReasonLeavesTheConsumerAlreadyOnTheTunerAlone()
    {
        var manager = Manager(new CountingDeviceFactory());

        var first = Started(manager, Request("s-1", SessionPurpose.Recording));

        var refused = manager.Begin(Request("s-2", SessionPurpose.Live, channel: 57));

        Assert.Equal(SessionRefusal.NoDeviceFree, refused.Refusal);
        Assert.Equal(SessionState.Active, first.State);
        Assert.False(first.Concluded);
        Assert.Single(manager.Sessions);

        first.Stop();
        first.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ATunerIsKeptForALittleWhileForWhoeverComesStraightBack()
    {
        var factory = new CountingDeviceFactory();
        var manager = Manager(factory, grace: TimeSpan.FromSeconds(5));

        var first = Started(manager, Request("s-1", SessionPurpose.Live));
        first.Stop();
        first.WaitForEnd(Deadlock);

        clock.Advance(TimeSpan.FromSeconds(4));

        var second = Started(manager, Request("s-2", SessionPurpose.Live));

        Assert.Equal(["adapter0"], factory.Opened);

        second.Stop();
        second.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ATunerHeldPastItsGraceIsTunedAgainForTheNextConsumer()
    {
        var factory = new CountingDeviceFactory();
        var manager = Manager(factory, grace: TimeSpan.FromSeconds(5));

        var first = Started(manager, Request("s-1", SessionPurpose.Live));
        first.Stop();
        first.WaitForEnd(Deadlock);

        clock.Advance(TimeSpan.FromSeconds(6));

        var second = Started(manager, Request("s-2", SessionPurpose.Live));

        Assert.Equal(["adapter0", "adapter0"], factory.Opened);

        second.Stop();
        second.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ATuneThatFailedLeavesTheTunerHeldRatherThanFreeForTheNextTry()
    {
        var factory = new RefusingDeviceFactory();
        var manager = Manager(factory, grace: TimeSpan.FromSeconds(5));

        var first = manager.Begin(Request("s-1", SessionPurpose.Live));

        Assert.Equal(SessionRefusal.DeviceUnavailable, first.Refusal);

        var second = manager.Begin(Request("s-2", SessionPurpose.Recording, channel: 57));

        Assert.Equal(SessionRefusal.NoDeviceFree, second.Refusal);
        Assert.Contains("would not lock", second.Detail, StringComparison.Ordinal);
        Assert.Equal(1, factory.Attempts);
    }

    [Fact]
    public void ATunerHeldBackAfterAFailedTuneIsTriedAgainOnceItsHoldRunsOut()
    {
        var factory = new RefusingDeviceFactory();
        var manager = Manager(factory, grace: TimeSpan.FromSeconds(5));

        manager.Begin(Request("s-1", SessionPurpose.Live));

        clock.Advance(TimeSpan.FromSeconds(6));

        manager.Begin(Request("s-2", SessionPurpose.Live));

        Assert.Equal(2, factory.Attempts);
    }

    [Fact]
    public void OneSlowTuneDoesNotHoldUpARequestForAnotherTuner()
    {
        var factory = new GatedDeviceFactory("adapter0");
        var manager = Manager(
            factory,
            Configuration(
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Terrestrial)
            )
        );

        var answered = new ManualResetEventSlim(false);
        var slow = new Thread(() =>
        {
            manager.Begin(Request("s-1", SessionPurpose.Live));
            answered.Set();
        })
        {
            IsBackground = true,
        };

        slow.Start();

        Assert.True(
            factory.Tuning.Wait(Deadlock),
            "The first request never reached the tuner."
        );
        Assert.False(answered.IsSet);

        var taken = string.Empty;
        var served = new ManualResetEventSlim(false);
        var quick = new Thread(() =>
        {
            var start = manager.Begin(Request("s-2", SessionPurpose.Live, channel: 57));

            if (start.TryGetSession(out var session))
            {
                taken = session.DeviceId;
            }

            served.Set();
        })
        {
            IsBackground = true,
        };

        quick.Start();

        Assert.True(
            served.Wait(Deadlock),
            "A request for another tuner waited behind a tune that had not finished."
        );
        Assert.Equal("adapter1", taken);
        Assert.False(answered.IsSet);

        factory.Finish.Set();

        Assert.True(answered.Wait(Deadlock));
        Assert.True(slow.Join(Deadlock));
        Assert.True(quick.Join(Deadlock));

        foreach (var session in manager.Sessions)
        {
            session.Stop();
            session.WaitForEnd(Deadlock);
        }
    }

    private sealed class CountingDeviceFactory : ITunerDeviceFactory
    {
        private readonly ConcurrentQueue<string> opened = new();

        public IReadOnlyCollection<string> Opened => [.. opened];

        public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
        {
            opened.Enqueue(device.Id!);

            return new ScriptedTunerDevice();
        }
    }

    private sealed class OneDeviceFactory(ITunerDevice device) : ITunerDeviceFactory
    {
        public ITunerDevice Create(DeviceSettings settings, TuningRequest tuning, TuneParams? tune) =>
            device;
    }

    private sealed class RefusingDeviceFactory : ITunerDeviceFactory
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
        {
            Interlocked.Increment(ref attempts);

            throw new IOException("the frontend would not lock");
        }
    }

    private sealed class GatedDeviceFactory(string slowDeviceId) : ITunerDeviceFactory
    {
        public ManualResetEventSlim Tuning { get; } = new(false);

        public ManualResetEventSlim Finish { get; } = new(false);

        public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
        {
            if (string.Equals(device.Id, slowDeviceId, StringComparison.Ordinal))
            {
                Tuning.Set();
                Finish.Wait(Deadlock);
            }

            return new ScriptedTunerDevice();
        }
    }
}
