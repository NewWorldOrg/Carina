using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SingleRecordingWriterTests : IDisposable
{
    private const int Rounds = 40;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-onewriter-").FullName;
    private readonly ManualTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [
                new OutputRootSettings("primary", root),
                new OutputRootSettings("archive", root),
            ],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter3", DeviceKind.Terrestrial),
            ]
        );

    private TunerSessionManager Manager() =>
        new(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

    private static StartSessionRequest Recording(
        string sessionId,
        string recordingId,
        int channel,
        string outputRoot = "primary"
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            OutputRoot = outputRoot,
            RecordingId = recordingId,
            EndsAt = Start.AddHours(1),
        };

    private static StartSessionRequest Watching(string sessionId, int channel) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            EndsAt = Start.AddHours(1),
        };

    private static void LetGo(TunerSessionManager manager)
    {
        foreach (TunerSession session in manager.Sessions)
        {
            session.Dispose();
        }
    }

    [Fact]
    public void OneRecordingUnderTwoOutputRootsIsStillOneRecording()
    {
        TunerSessionManager manager = Manager();

        SessionStart first = manager.Begin(Recording("s-1", "k-shared", 55));

        Assert.True(first.TryGetSession(out _), first.Detail);

        SessionStart second = manager.Begin(Recording("s-2", "k-shared", 56, "archive"));

        Assert.Equal(SessionRefusal.RecordingAlreadyExists, second.Refusal);
        Assert.Contains("k-shared", second.Detail, StringComparison.Ordinal);

        LetGo(manager);
    }

    [Fact]
    public void TwoDifferentRecordingsUnderOneOutputRootBothStart()
    {
        TunerSessionManager manager = Manager();

        SessionStart first = manager.Begin(Recording("s-1", "k-one", 55));
        SessionStart second = manager.Begin(Recording("s-2", "k-two", 56));

        Assert.True(first.TryGetSession(out _), first.Detail);
        Assert.True(second.TryGetSession(out _), second.Detail);

        LetGo(manager);
    }

    [Fact]
    public void ARecordingWhoseSessionHasEndedCanBeTakenUpAgain()
    {
        TunerSessionManager manager = Manager();

        SessionStart first = manager.Begin(Recording("s-1", "k-resume", 55));

        Assert.True(first.TryGetSession(out TunerSession? started), first.Detail);

        started.Stop();
        started.WaitForEnd(Deadlock);

        SessionStart again = manager.Begin(Recording("s-2", "k-resume", 55));

        Assert.True(again.TryGetSession(out _), again.Detail);

        LetGo(manager);
    }

    [Fact]
    public async Task TwoRequestsForOneRecordingRaceAndExactlyOneOfThemStarts()
    {
        int refusedForTheRightReason = 0;

        for (int round = 0; round < Rounds; round++)
        {
            TunerSessionManager manager = Manager();
            string recordingId = $"k-race-{round}";
            var lineUp = new Barrier(2);
            SessionStart[] outcomes = new SessionStart[2];

            Task[] both =
            [
                Task.Run(() =>
                {
                    lineUp.SignalAndWait();
                    outcomes[0] = manager.Begin(Recording($"a-{round}", recordingId, 55));
                }),
                Task.Run(() =>
                {
                    lineUp.SignalAndWait();
                    outcomes[1] = manager.Begin(Recording($"b-{round}", recordingId, 56));
                }),
            ];

            await Task.WhenAll(both).WaitAsync(Deadlock);

            SessionStart[] started = [.. outcomes.Where(outcome => outcome.Refusal is SessionRefusal.None)];

            Assert.Single(started);

            SessionStart turnedAway = outcomes.Single(outcome => outcome.Refusal is not SessionRefusal.None);

            if (turnedAway.Refusal is SessionRefusal.RecordingAlreadyExists)
            {
                refusedForTheRightReason++;
            }

            Assert.Single(
                Directory.GetFiles(root),
                file => string.Equals(
                    Path.GetFileName(file),
                    $"{recordingId}.ts",
                    StringComparison.Ordinal
                )
            );

            LetGo(manager);
        }

        Assert.Equal(Rounds, refusedForTheRightReason);
    }

    [Fact]
    public async Task AnOutputRootThatDoesNotAnswerHoldsUpNothingButItsOwnSession()
    {
        var stuck = new StallingRecordingWriterFactory();
        var manager = new TunerSessionManager(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: stuck
        );

        Task<SessionStart> hanging = Task.Run(() => manager.Begin(Recording("s-1", "k-stuck", 55)));

        stuck.AwaitOpening(Deadlock);

        SessionStart other = await Task.Run(() => manager.Begin(Watching("s-2", 56)))
            .WaitAsync(Deadlock);

        Assert.True(other.TryGetSession(out _), other.Detail);

        await Task.Run(manager.EnterDraining).WaitAsync(Deadlock);

        Assert.True(manager.IsDraining);

        stuck.LetGo();

        SessionStart late = await hanging.WaitAsync(Deadlock);

        Assert.Equal(SessionRefusal.Draining, late.Refusal);

        LetGo(manager);
    }

    [Fact]
    public void TheFileSystemItselfLetsTwoWritersOntoOneRecording()
    {
        using var first = new RecordingWriter(root, "k-unguarded");

        Exception? refused = Record.Exception(() =>
        {
            using var second = new RecordingWriter(root, "k-unguarded");

            second.Write([0x47]);
        });

        Assert.Null(refused);
    }

    [Fact]
    public void TheSecondWriterIsNeverOpenedAtAllAndItsTunerGoesBack()
    {
        var writers = new CountingRecordingWriterFactory();
        var manager = new TunerSessionManager(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: writers
        );

        SessionStart first = manager.Begin(Recording("s-1", "k-shared", 55));

        Assert.True(first.TryGetSession(out _), first.Detail);

        SessionStart refused = manager.Begin(Recording("s-2", "k-shared", 56));

        Assert.Equal(SessionRefusal.RecordingAlreadyExists, refused.Refusal);
        Assert.Equal(1, writers.Opened);
        Assert.Single(manager.Sessions);

        SessionStart next = manager.Begin(Recording("s-3", "k-other", 56));

        Assert.True(next.TryGetSession(out _), next.Detail);
        Assert.Equal(2, writers.Opened);

        LetGo(manager);
    }
}
