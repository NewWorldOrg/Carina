using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SingleTunerSweepTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan LetsGoSlowly = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly int[] Sweep = [13, 14, 15, 16];

    private readonly ManualTimeProvider clock = new(Start);

    [Fact]
    public async Task EveryChannelOfASweepIsReachedWhenOneTunerServesThemAll()
    {
        var manager = Manager();
        var reached = new List<int>();

        foreach (var channel in Sweep)
        {
            var start = manager.Begin(Request($"scan-{channel}", channel));

            if (!start.TryGetSession(out var session))
            {
                break;
            }

            reached.Add(channel);

            await manager.StopAsync(session.SessionId, "test", CancellationToken.None);
        }

        Assert.Equal(Sweep, reached);
    }

    [Fact]
    public async Task TheTunerIsFreeByTheTimeTheStopIsAnswered()
    {
        var manager = Manager();
        var start = manager.Begin(Request("scan-13", 13));

        Assert.True(start.TryGetSession(out var session));

        var outcome = await manager.StopAsync(session.SessionId, "test", CancellationToken.None);

        Assert.Equal(SessionStopOutcome.Stopped, outcome);
        Assert.True(session.Concluded);
        Assert.False(manager.IsClaimed("adapter0"));
    }

    [Fact]
    public async Task AStopThatCouldNotFreeTheTunerInTimeSaysSoInsteadOfClaimingItDid()
    {
        var device = new HeldOpenTunerDevice();
        var manager = Manager(
            new OneTunerDeviceFactory(device),
            letGoLimit: TimeSpan.FromMilliseconds(50)
        );
        var start = manager.Begin(Request("scan-13", 13));

        Assert.True(start.TryGetSession(out var session));
        Assert.True(
            device.Reading.Wait(Deadlock),
            "The session never reached the read that cannot be interrupted."
        );

        var outcome = await manager.StopAsync(session.SessionId, "test", CancellationToken.None);

        Assert.Equal(SessionStopOutcome.Stopping, outcome);
        Assert.False(session.Concluded);
        Assert.True(manager.IsClaimed("adapter0"));

        device.LetGo();
        session.WaitForEnd(Deadlock);

        Assert.Equal(
            SessionStopOutcome.AlreadyEnded,
            await manager.StopAsync(session.SessionId, "test", CancellationToken.None)
        );
    }

    [Fact]
    public void TheStopBoundOutlastsTheDeviceReadThatCannotBeInterrupted() =>
        Assert.True(TunerSessionManager.LetGoLimit > DvbTunerSettings.Default.BytePatience);

    private TunerSessionManager Manager() => Manager(new StubbornTunerDeviceFactory(LetsGoSlowly));

    private TunerSessionManager Manager(ITunerDeviceFactory factory, TimeSpan? letGoLimit = null) =>
        new(
            new DriverConfiguration(
                "/run/carina/driver.sock",
                [],
                6,
                new TunerSettings(TunerBackend.Fake),
                [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
            ),
            factory,
            clock,
            NullLogger<TunerSessionManager>.Instance,
            letGoLimit: letGoLimit
        );

    private static StartSessionRequest Request(string sessionId, int channel) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Scan,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            EndsAt = Start.AddHours(1),
        };
}
