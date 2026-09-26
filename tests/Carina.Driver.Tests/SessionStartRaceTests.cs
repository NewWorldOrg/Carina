using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

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
}
