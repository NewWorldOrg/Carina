using Carina.Contracts;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveStraySweepTests
{
    private const string Device = "adapter3.frontend0";

    private readonly LiveDriverStandIn driver = new();

    private readonly LiveLeases leases = new();

    [Fact]
    public async Task BrPs001ALiveSessionTheDriverHoldsThatNoViewerHereIsBehindIsLetGo()
    {
        SessionId left = Held("live-left-behind");

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Equal([(left, LiveStraySweep.LetGoBecause)], driver.Stopped);
    }

    [Fact]
    public async Task BrPs001ALiveSessionThisAppHoldsALeaseOnIsLeftWhereItIs()
    {
        SessionId ours = Held("live-being-watched");
        leases.Take(ours);

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task BrPs001ARecordingIsNeverLetGoHoweverLittleThisAppKnowsOfIt()
    {
        Held("rec-in-progress", SessionPurpose.Recording);
        Held("epg-survey", SessionPurpose.Survey);

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task BrPs001ASessionTheDriverHasAlreadyConcludedIsNotStoppedASecondTime()
    {
        Held("live-already-over", concluded: true);

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task BrPs001ADriverThatCannotBeReachedHasNothingLetGoOfRatherThanEverythingGuessedAt()
    {
        Held("live-left-behind");
        driver.Unreachable = true;

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task BrPs001OnlyTheStraysAreLetGoWhenSomeOfWhatTheDriverHoldsIsOurs()
    {
        SessionId ours = Held("live-ours");
        SessionId theirs = Held("live-theirs");
        Held("rec-running", SessionPurpose.Recording);
        leases.Take(ours);

        await Sweep().SweepAsync(CancellationToken.None);

        Assert.Equal([(theirs, LiveStraySweep.LetGoBecause)], driver.Stopped);
    }

    [Fact]
    public async Task BrPs001ASweepThatFoundTheDriverGoneIsFollowedByTheNextOneRatherThanBeingTheLast()
    {
        SessionId left = Held("live-left-behind");
        driver.Unreachable = true;

        var clock = new HandTurnedClock();
        LiveStraySweep sweeping = Sweep(clock);

        await sweeping.StartAsync(CancellationToken.None);
        await Eventually.Happens(() => clock.Pending > 0, "the sweep waits for its first pass");

        clock.Turn(new LiveStraySettings().BeforeFirstSweep);
        await Eventually.Happens(() => driver.ActiveAsked > 0, "the first pass asks the driver");
        Assert.Empty(driver.Stopped);

        driver.Unreachable = false;
        await Eventually.Happens(() => clock.Pending > 0, "the sweep waits for its second pass");
        clock.Turn(new LiveStraySettings().BetweenSweeps);

        await Eventually.Happens(() => driver.Stopped.Count is 1, "the second pass lets the stray go");
        await sweeping.StopAsync(CancellationToken.None);

        Assert.Equal([(left, LiveStraySweep.LetGoBecause)], driver.Stopped);
    }

    private SessionId Held(string id, SessionPurpose purpose = SessionPurpose.Live, bool concluded = false)
    {
        SessionId session = SessionId.Parse(id);

        driver.Active.Add(new SessionSnapshot(
            session,
            purpose,
            Device,
            concluded ? SessionState.Stopped : SessionState.Active,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(4))
        {
            Concluded = concluded,
        });

        return session;
    }

    private LiveStraySweep Sweep(TimeProvider? clock = null)
        => new(
            driver,
            leases,
            new LiveStraySettings(),
            clock ?? TimeProvider.System,
            NullLogger<LiveStraySweep>.Instance);
}
