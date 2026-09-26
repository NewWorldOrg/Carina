using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Recordings.RecordingTickFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class ProgramExtensionFollowerTests
{
    private static readonly DateTime Now = Airs.AddMinutes(10);

    private static readonly ProgrammeId Broadcast = new(new NetworkId(32736), new ServiceId(1025), new EventId(9));

    [Fact]
    public async Task AProgrammeThatRunsLaterCarriesTheLedgerAndTheDriverWithIt()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        IReadOnlyList<RecordingFollowed> followed = await Follow(
            recordings,
            driver,
            Announcing(Airs.AddMinutes(45)),
            running);

        Assert.Equal(Airs.AddMinutes(45), Assert.Single(followed).EndsAt);
        Assert.Equal(Airs.AddMinutes(45), running.ExpectedWindowEnd);
        Assert.Equal(
            (RecordingSessions.Named(running.Id), new DateTimeOffset(Airs.AddMinutes(45), TimeSpan.Zero)),
            Assert.Single(driver.Extended));
        Assert.Equal(running.Id, Assert.Single(recordings.Saved));
    }

    [Fact]
    public async Task TheSameEndIsNotAskedForTwice()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        HeldProgrammes guide = Announcing(Airs.AddMinutes(45));

        Assert.Single(await Follow(recordings, driver, guide, running));
        Assert.Empty(await Follow(recordings, driver, guide, running));
        Assert.Single(driver.Extended);
    }

    [Fact]
    public async Task AProgrammeThatSaysItWillFinishSoonerIsNotFollowedBack()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();

        Assert.Empty(await Follow(recordings, driver, Announcing(Airs.AddMinutes(20)), running));
        Assert.Empty(driver.Extended);
        Assert.Equal(Airs.AddMinutes(30), running.ExpectedWindowEnd);
    }

    [Fact]
    public async Task AGuideThatHasGoneQuietEndsNothingAndMovesNothing()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();

        Assert.Empty(await Follow(recordings, driver, new HeldProgrammes(), running));
        Assert.Empty(driver.Extended);
        Assert.Empty(driver.Stopped);
        Assert.Equal(Airs.AddMinutes(30), running.ExpectedWindowEnd);
        Assert.True(running.IsInFlight);
    }

    [Fact]
    public async Task AnEndNobodyAnnouncedIsCarriedForwardAndSaidSo()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Now.AddMinutes(5));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        IReadOnlyList<RecordingFollowed> followed = await Follow(
            recordings,
            driver,
            Announcing(null),
            running);

        Assert.True(Assert.Single(followed).EndUndecided);
        Assert.Equal(Now + RecordingSettings.HoldingAnUnannouncedEnd, running.ExpectedWindowEnd);
        Assert.Equal(
            RecordingFault.EndStillUndecided,
            Assert.Single(running.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task TheReasonAnEndWasNeverAnnouncedIsWrittenOnlyOnce()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Now.AddMinutes(5));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        HeldProgrammes guide = Announcing(null);

        Assert.Single(await Follow(recordings, driver, guide, running));
        Assert.Single(await Follow(recordings, driver, guide, running, at: Now.AddMinutes(15)));
        Assert.Single(running.OutcomeDetail);
        Assert.Equal(2, driver.Extended.Count);
    }

    [Fact]
    public async Task ADriverThatWillNotHoldTheTunerLongerLeavesTheWindowAsItWas()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver
        {
            RefusesToExtend = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.SessionEnded, [])),
        };

        Assert.Empty(await Follow(recordings, driver, Announcing(Airs.AddMinutes(45)), running));
        Assert.Equal(Airs.AddMinutes(30), running.ExpectedWindowEnd);
        Assert.Empty(recordings.Saved);
    }

    [Fact]
    public async Task TheLedgerTakesTheEndTheDriverPromisedRatherThanTheOneItWasAskedFor()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver { ExtendsByLessThanAsked = TimeSpan.FromMinutes(10) };
        IReadOnlyList<RecordingFollowed> followed = await Follow(
            recordings,
            driver,
            Announcing(Airs.AddMinutes(45)),
            running);

        Assert.Equal(Airs.AddMinutes(35), Assert.Single(followed).EndsAt);
        Assert.Equal(Airs.AddMinutes(35), running.ExpectedWindowEnd);
    }

    [Fact]
    public async Task ARiderCutBackToWhatItAlreadyHoldsIsNotWrittenAtAll()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver { ExtendsByLessThanAsked = TimeSpan.FromMinutes(20) };

        Assert.Empty(await Follow(recordings, driver, Announcing(Airs.AddMinutes(45)), running));
        Assert.Equal(Airs.AddMinutes(30), running.ExpectedWindowEnd);
        Assert.Empty(recordings.Saved);
    }

    [Fact]
    public async Task ARecordingAlreadyAskedToStopIsLeftAlone()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        running.Abort(Now);
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();

        Assert.Empty(await Follow(recordings, driver, Announcing(Airs.AddMinutes(45)), running));
        Assert.Empty(driver.Extended);
    }

    [Fact]
    public async Task ABroadcastTheGuideNoLongerAnnouncesIsNotCarriedForward()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Now.AddMinutes(5));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        HeldProgrammes guide = Announcing(null);
        guide.Programmes[0].Heard(Airs.AddMinutes(-30));
        guide.Programmes.Add(Elsewhere(Airs));

        Assert.Empty(await Follow(recordings, driver, guide, running));
        Assert.Empty(driver.Extended);
        Assert.True(running.IsInFlight);
    }

    [Fact]
    public async Task ARecordingNoReservationAsksForIsNotFollowed()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();

        Assert.Empty(await Follower(recordings, driver, Announcing(Airs.AddMinutes(45)))
            .FollowAsync([running], [], Now, CancellationToken.None));
        Assert.Empty(driver.Extended);
    }

    [Fact]
    public async Task ADriverThatWouldNotHoldTheTunerIsNotAskedTheSameThingEveryTick()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver
        {
            RefusesToExtend = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.SessionEnded, [])),
        };
        ProgramExtensionFollower follower = Follower(recordings, driver, Announcing(Airs.AddMinutes(45)));

        Assert.Empty(await Tick(follower, running));
        Assert.Empty(await Tick(follower, running));
        Assert.Single(driver.Extended);
        Assert.Equal(Airs.AddMinutes(30), running.ExpectedWindowEnd);
    }

    [Fact]
    public async Task AnEndTheDriverCutBackIsNotPutToItAgainUntilTheProgrammeRunsLaterStill()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver { ExtendsByLessThanAsked = TimeSpan.FromMinutes(10) };
        HeldProgrammes guide = Announcing(Airs.AddMinutes(45));
        ProgramExtensionFollower follower = Follower(recordings, driver, guide);

        Assert.Single(await Tick(follower, running));
        Assert.Equal(Airs.AddMinutes(35), running.ExpectedWindowEnd);

        Assert.Empty(await Tick(follower, running));
        Assert.Single(driver.Extended);

        NowRunsUntil(guide, Airs.AddMinutes(60));

        Assert.Single(await Tick(follower, running));
        Assert.Equal(2, driver.Extended.Count);
        Assert.Equal(Airs.AddMinutes(50), running.ExpectedWindowEnd);
    }

    [Fact]
    public async Task AnAskThatNeverReachedTheDriverIsPutToItAgainOnTheNextTick()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver
        {
            RefusesToExtend = DriverCall<SessionSnapshot>.Unreachable("the driver did not answer"),
        };
        ProgramExtensionFollower follower = Follower(recordings, driver, Announcing(Airs.AddMinutes(45)));

        Assert.Empty(await Tick(follower, running));
        Assert.Empty(await Tick(follower, running));
        Assert.Equal(2, driver.Extended.Count);
    }

    [Fact]
    public async Task AGuideThatCannotBeReadForOneRecordingLeavesTheOthersFollowed()
    {
        var recordings = new HeldRecordings();
        Recording first = InFlight(Airs, Airs.AddMinutes(30));
        Recording second = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(first);
        recordings.Rows.Add(second);

        var driver = new RecordingDriver();
        var guide = new GuideThatRefusesOnce(Announcing(Airs.AddMinutes(45)));

        IReadOnlyList<RecordingFollowed> followed = await new ProgramExtensionFollower(
                recordings,
                guide,
                driver,
                new EndsAlreadyAsked(),
                Settings,
                NullLogger<ProgramExtensionFollower>.Instance)
            .FollowAsync(
                [first, second],
                [
                    Due(9, startedAt: Airs) with { Id = Named(first) },
                    Due(9, startedAt: Airs) with { Id = Named(second) },
                ],
                Now,
                CancellationToken.None);

        Assert.Equal(second.Id, Assert.Single(followed).Id);
        Assert.Equal(Airs.AddMinutes(30), first.ExpectedWindowEnd);
        Assert.Equal(Airs.AddMinutes(45), second.ExpectedWindowEnd);
    }

    [Fact]
    public async Task AnEndTheLedgerCouldNotWriteDownIsAskedForAgainOnTheNextTick()
    {
        HeldRecordings recordings = new() { RefusingToSave = new InvalidOperationException("moved meanwhile") };
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        RecordingDriver driver = new();
        EndsAlreadyAsked asked = new();
        HeldProgrammes guide = Announcing(Airs.AddMinutes(45));

        Assert.Empty(await Tick(Follower(recordings, driver, guide, asked), running));
        Assert.Single(driver.Extended);
        Assert.False(asked.AlreadyPut(running.Id, Airs.AddMinutes(45)));
    }

    [Fact]
    public async Task ARecordingThatIsNoLongerRunningIsForgottenRatherThanRememberedForever()
    {
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        recordings.Rows.Add(running);

        var driver = new RecordingDriver { ExtendsByLessThanAsked = TimeSpan.FromMinutes(20) };
        var asked = new EndsAlreadyAsked();
        HeldProgrammes guide = Announcing(Airs.AddMinutes(45));

        Assert.Empty(await Tick(Follower(recordings, driver, guide, asked), running));
        Assert.Single(driver.Extended);

        await Follower(recordings, driver, guide, asked).FollowAsync([], [], Now, CancellationToken.None);

        Assert.Empty(await Tick(Follower(recordings, driver, guide, asked), running));
        Assert.Equal(2, driver.Extended.Count);
    }

    private static async Task<IReadOnlyList<RecordingFollowed>> Follow(
        HeldRecordings recordings,
        RecordingDriver driver,
        HeldProgrammes programmes,
        Recording running,
        DateTime? at = null,
        TimeSpan? marginAfter = null)
        => await Follower(recordings, driver, programmes).FollowAsync(
            [running],
            [Due(9, startedAt: Airs, marginAfter: marginAfter) with { Id = Named(running) }],
            at ?? Now,
            CancellationToken.None);

    private static ReservationId Named(Recording running)
        => running.ReservationId ?? throw new InvalidOperationException("This recording belongs to no reservation.");

    private static ProgramExtensionFollower Follower(
        HeldRecordings recordings,
        RecordingDriver driver,
        HeldProgrammes programmes,
        EndsAlreadyAsked? asked = null)
        => new(
            recordings,
            programmes,
            driver,
            asked ?? new EndsAlreadyAsked(),
            Settings,
            NullLogger<ProgramExtensionFollower>.Instance);

    private static Task<IReadOnlyList<RecordingFollowed>> Tick(
        ProgramExtensionFollower follower,
        Recording running,
        DateTime? at = null)
        => follower.FollowAsync(
            [running],
            [Due(9, startedAt: Airs) with { Id = Named(running) }],
            at ?? Now,
            CancellationToken.None);

    private static void NowRunsUntil(HeldProgrammes guide, DateTime endsAt)
        => guide.Programmes[0].Absorb(
            new ProgrammeBroadcast(
                Broadcast,
                new TransportStreamId(32736),
                Airs,
                endsAt,
                "A programme",
                "What it is about",
                false),
            Now);

    private static HeldProgrammes Announcing(DateTime? endsAt)
    {
        var programmes = new HeldProgrammes();

        programmes.Programmes.Add(Programme.Discover(
            new ProgrammeBroadcast(
                Broadcast,
                new TransportStreamId(32736),
                Airs,
                endsAt,
                "A programme",
                "What it is about",
                false),
            Airs.AddHours(-3)));

        return programmes;
    }

    private static Programme Elsewhere(DateTime heardAt)
    {
        Programme programme = Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(Broadcast.NetworkId, Broadcast.ServiceId, new EventId(10)),
                new TransportStreamId(32736),
                Airs.AddHours(2),
                Airs.AddHours(3),
                "The next programme",
                string.Empty,
                false),
            heardAt);

        programme.Heard(heardAt);

        return programme;
    }
}
