using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SessionStartRaceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-start-race-").FullName;

    private readonly SteppedTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration() =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    private static StartSessionRequest Request(
        string sessionId,
        SessionPurpose purpose,
        int channel = 55
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = purpose,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            OutputRoot = purpose is SessionPurpose.Recording ? "primary" : null,
            EndsAt = Start.AddHours(1),
            RecordingId = purpose is SessionPurpose.Recording ? $"k-{sessionId}" : null,
        };

    private static Thread Background(Action work)
    {
        Thread thread = new(() => work()) { IsBackground = true };
        thread.Start();

        return thread;
    }

    private void AwaitAnsweredOrWaitingOnTheClock(ManualResetEventSlim answered)
    {
        DateTime deadline = DateTime.UtcNow + Deadlock;

        while (!answered.IsSet && clock.Waiting is 0)
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                "The second start neither answered nor waited for anything."
            );

            Thread.Sleep(1);
        }
    }

    [Fact]
    public void ASecondStartUnderTheSameNameIsTurnedAwayWithoutLettingGoOfTheTunerTheFirstHolds()
    {
        StallingRecordingWriterFactory writers = new();
        TunerSessionManager manager = new(
            Configuration(),
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: writers
        );

        SessionStart? first = null;
        Thread firstThread = Background(() => first = manager.Begin(Request("r-1", SessionPurpose.Recording)));

        writers.AwaitOpening(Deadlock);

        SessionStart? second = null;
        ManualResetEventSlim answered = new(false);
        Thread secondThread = Background(() =>
        {
            second = manager.Begin(Request("r-1", SessionPurpose.Recording));
            answered.Set();
        });

        AwaitAnsweredOrWaitingOnTheClock(answered);
        writers.LetGo();

        Assert.True(firstThread.Join(Deadlock));
        Assert.True(secondThread.Join(Deadlock));

        Assert.Equal(SessionRefusal.DuplicateSession, second!.Refusal);
        Assert.True(first!.TryGetSession(out TunerSession? recording), first.Detail);
        Assert.Equal(SessionState.Active, recording.State);
        Assert.True(manager.IsClaimed("adapter0"));

        recording.Stop();
        recording.WaitForEnd(Deadlock);
    }

    [Fact]
    public void ARecordingThatTakesATunerStillBeingTunedWaitsForThatTuneAndThenTunesItItself()
    {
        OneFrontendDeviceFactory factory = new();
        TunerSessionManager manager = new(
            Configuration(),
            factory,
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: new CountingRecordingWriterFactory(),
            tunerGrace: TimeSpan.FromSeconds(5)
        );

        SessionStart? sweep = null;
        Thread sweeping = Background(() => sweep = manager.Begin(Request("s-1", SessionPurpose.Survey)));

        Assert.True(factory.FirstTuning.Wait(Deadlock), "The sweep never reached the tuner.");

        SessionStart? recording = null;
        ManualResetEventSlim answered = new(false);
        Thread recordingThread = Background(() =>
        {
            recording = manager.Begin(Request("r-1", SessionPurpose.Recording, channel: 57));
            answered.Set();
        });

        AwaitAnsweredOrWaitingOnTheClock(answered);
        factory.LetTheFirstFinish.Set();

        Assert.True(sweeping.Join(Deadlock));
        Assert.True(recordingThread.Join(Deadlock));

        Assert.Equal(SessionRefusal.DeviceBusy, sweep!.Refusal);
        Assert.True(recording!.TryGetSession(out TunerSession? recorder), recording.Detail);
        Assert.Equal(2, factory.Devices.Count);
        Assert.True(factory.Devices[0].Disposed);
        Assert.False(factory.Devices[1].Disposed);
        Assert.True(manager.IsClaimed("adapter0"));
        Assert.False(manager.IsFaulted("adapter0", out _));

        recorder.Stop();
        recorder.WaitForEnd(Deadlock);

        Assert.Equal(SessionStopReason.Requested, recorder.StopReason);
    }

    private sealed class OneFrontendDeviceFactory : ITunerDeviceFactory
    {
        private readonly Lock gate = new();
        private readonly List<FrontendTunerDevice> devices = [];

        private int holding;
        private int opened;

        public ManualResetEventSlim FirstTuning { get; } = new(false);

        public ManualResetEventSlim LetTheFirstFinish { get; } = new(false);

        public IReadOnlyList<FrontendTunerDevice> Devices
        {
            get
            {
                lock (gate)
                {
                    return [.. devices];
                }
            }
        }

        public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
        {
            if (Interlocked.Increment(ref holding) > 1)
            {
                Interlocked.Decrement(ref holding);

                throw new IOException("Device or resource busy");
            }

            if (Interlocked.Increment(ref opened) is 1)
            {
                FirstTuning.Set();
                LetTheFirstFinish.Wait(Deadlock);
            }

            FrontendTunerDevice created = new(() => Interlocked.Decrement(ref holding));

            lock (gate)
            {
                devices.Add(created);
            }

            return created;
        }
    }

    private sealed class FrontendTunerDevice(Action released) : ITunerDevice
    {
        private readonly FakeTunerDevice inner = new(55, 50001);

        private int disposed;

        public long Overflows => 0;

        public bool Disposed => Volatile.Read(ref disposed) is 1;

        public byte[] Read(int count, CancellationToken cancellationToken)
        {
            if (Disposed)
            {
                throw new IOException("Bad file descriptor");
            }

            return inner.Read(count, cancellationToken);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) is 0)
            {
                released();
            }
        }
    }
}
