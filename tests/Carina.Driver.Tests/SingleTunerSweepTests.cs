using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SingleTunerSweepTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan LetsGoSlowly = TimeSpan.FromMilliseconds(500);

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

            await manager.StopAsync(session.SessionId, CancellationToken.None);
        }

        Assert.Equal(Sweep, reached);
    }

    [Fact]
    public async Task TheTunerIsFreeByTheTimeTheStopIsAnswered()
    {
        var manager = Manager();
        var start = manager.Begin(Request("scan-13", 13));

        Assert.True(start.TryGetSession(out var session));

        var outcome = await manager.StopAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(SessionStopOutcome.Stopped, outcome);
        Assert.True(session.Concluded);
        Assert.False(manager.IsClaimed("adapter0"));
    }

    private TunerSessionManager Manager() =>
        new(
            new DriverConfiguration(
                "/run/carina/driver.sock",
                [],
                6,
                new TunerSettings(TunerBackend.Fake),
                [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
            ),
            new StubbornTunerDeviceFactory(LetsGoSlowly),
            clock,
            NullLogger<TunerSessionManager>.Instance
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
