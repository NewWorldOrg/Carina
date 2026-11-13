using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Fixtures.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

using Puppis.Contracts;

using static Carina.Infrastructure.Tests.Fixtures.Recordings.RecordingTickFixture;

using HeldRecordings = Carina.Infrastructure.Tests.Fixtures.Recordings.HeldRecordings;

namespace Carina.Infrastructure.Tests.Unit.Infra.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingRoundTests
{
    private const long RoomForTheOneStarting = 3_681_562_500L;

    private const long RoomForItAndTheOneAlreadyRunning = 6_156_562_500L;

    private static readonly RetryPolicy Policy = new(2, TimeSpan.FromMinutes(1));

    private static readonly DriverCall<SessionSnapshot> WouldNotLock =
        DriverCall<SessionSnapshot>.Refused(new DriverProblem(SessionRefusalTitles.NoLock, []));

    [Fact]
    public async Task WhatIsOverIsStoppedBeforeWhatIsDueIsStarted()
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), Airs));

        var driver = new RecordingDriver();
        RecordingRun run = await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Single(run.Stopped);
        Assert.Single(run.Started);
        Assert.StartsWith("stop:", driver.Log[0], StringComparison.Ordinal);
        Assert.True(
            driver.Log.FindIndex(entry => entry.StartsWith("stop:", StringComparison.Ordinal))
            < driver.Log.FindIndex(entry => entry.StartsWith("start:", StringComparison.Ordinal)),
            "The tick reached for a tuner before it let go of the one the finished recording was holding.");
    }

    [Fact]
    public async Task ARecordingIsStoppedTheInstantItsWindowCloses()
    {
        Assert.Single(await Stopping(Airs));
        Assert.Single(await Stopping(Airs.AddTicks(-1)));
        Assert.Empty(await Stopping(Airs.AddTicks(1)));
    }

    [Fact]
    public async Task TheStopSaysWhyItIsStopping()
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), Airs));

        var driver = new RecordingDriver();

        await Round(Holding(), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Equal("the window this recording was promised has closed", Assert.Single(driver.StopReasons));
    }

    [Fact]
    public async Task TheStoppedRecordingIsTheOneTheDriverWasToldAbout()
    {
        var recordings = new HeldRecordings();
        Recording over = InFlight(Airs.AddMinutes(-30), Airs);
        recordings.Rows.Add(over);

        var driver = new RecordingDriver();

        await Round(Holding(), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Equal($"stop:rec-{over.Id.Wire}", Assert.Single(driver.Log));
        Assert.Equal(Airs, over.AbortedAt);
        Assert.Equal(over.Id, Assert.Single(recordings.Saved));
    }

    [Fact]
    public async Task AStopTheDriverNeverHeardIsLeftForTheNextTick()
    {
        var recordings = new HeldRecordings();
        Recording over = InFlight(Airs.AddMinutes(-30), Airs);
        recordings.Rows.Add(over);

        var driver = new RecordingDriver
        {
            RefusesToStop = DriverCall<SessionSnapshot>.Unreachable("the socket was not there"),
        };

        RecordingRun run = await Round(Holding(), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Empty(run.Stopped);
        Assert.Null(over.AbortedAt);
        Assert.Empty(recordings.Saved);
    }

    [Fact]
    public async Task ARecordingAlreadyAskedToStopIsNotAskedAgain()
    {
        var recordings = new HeldRecordings();
        Recording over = InFlight(Airs.AddMinutes(-30), Airs);
        over.Abort(Airs);
        recordings.Rows.Add(over);

        var driver = new RecordingDriver();
        RecordingRun run = await Round(Holding(), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Empty(run.Stopped);
        Assert.Empty(driver.Log);
    }

    [Fact]
    public async Task NothingIsStartedWithoutWinningTheClaim()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        reservations.Unclaimable.Add(due.Id.Value);

        var recordings = new HeldRecordings();
        var driver = new RecordingDriver();
        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Empty(run.Started);
        Assert.Empty(recordings.Rows);
        Assert.DoesNotContain(driver.Log, entry => entry.StartsWith("start:", StringComparison.Ordinal));
        Assert.Equal(RecordingRefusalKind.ClaimLostToAnother, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task AChannelThatCannotBeTunedIsNeverClaimed()
    {
        PlannedReservations reservations = Holding(Due(1));
        var driver = new RecordingDriver();

        RecordingRun run = await Round(
            reservations,
            new HeldRecordings(),
            driver,
            TuningResolution.Refused(TuningRefusal.NoSelectedChannel)).RunAsync(CancellationToken.None);

        Assert.Empty(reservations.Claimed);
        Assert.Empty(driver.Log);
        Assert.Equal(RecordingRefusalKind.TuningRefused, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task EveryWayTheDirectoryCanRefuseIsReportedAsTheClassItWas()
    {
        TuningRefusal[] refusals = [.. Enum.GetValues<TuningRefusal>().Where(refusal => refusal is not TuningRefusal.None)];
        List<TuningRefusal> reported = [];

        foreach (TuningRefusal refusal in refusals)
        {
            RecordingRun run = await Round(
                Holding(Due(1)),
                new HeldRecordings(),
                new RecordingDriver(),
                TuningResolution.Refused(refusal)).RunAsync(CancellationToken.None);

            reported.Add(Assert.Single(run.Refused).Refusal);
        }

        Assert.NotEmpty(refusals);
        Assert.Equal(refusals, reported);
        Assert.Equal(refusals.Length, reported.Distinct().Count());
    }

    [Fact]
    public async Task TheDriverIsAskedForASessionOnlyAfterTheClaimIsWon()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var driver = new RecordingDriver();

        await Round(reservations, new HeldRecordings(), driver).RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(reservations.Claimed));
        Assert.Single(driver.Started);
    }

    [Fact]
    public async Task ADriverThatWouldNotStartTheSessionGetsTheClaimGivenBack()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.NoDeviceFree, [])),
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(recordings.Rows);
        Assert.Empty(run.Started);
    }

    [Theory]
    [InlineData("noDeviceFree", RecordingRefusalKind.TunerContended)]
    [InlineData("deviceBusy", RecordingRefusalKind.TunerContended)]
    [InlineData("unknownOutputRoot", RecordingRefusalKind.DriverRefused)]
    [InlineData("draining", RecordingRefusalKind.DriverRefused)]
    public async Task ARefusalTheDriverNamedIsClassifiedByWhatItSaid(string title, RecordingRefusalKind expected)
    {
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(new DriverProblem(title, [])),
        };

        RecordingRefusal refusal = Assert.Single(
            (await Round(Holding(Due(1)), new HeldRecordings(), driver).RunAsync(CancellationToken.None)).Refused);

        Assert.Equal(expected, refusal.Kind);
        Assert.Equal(title, refusal.Note);
        Assert.Equal(TuningRefusal.None, refusal.Refusal);
    }

    [Fact]
    public async Task ADriverNobodyCouldReachIsNotTheSameAsOneThatRefused()
    {
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Unreachable("the socket was not there"),
        };

        RecordingRefusal refusal = Assert.Single(
            (await Round(Holding(Due(1)), new HeldRecordings(), driver).RunAsync(CancellationToken.None)).Refused);

        Assert.Equal(RecordingRefusalKind.DriverUnreachable, refusal.Kind);
    }

    [Fact]
    public async Task WhatTheDriverIsAskedForNamesTheRecordingAndTheRoomItGoesIn()
    {
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver();

        await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        StartSessionRequest asked = Assert.Single(driver.Started);
        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(SessionPurpose.Recording, asked.Purpose);
        Assert.Equal($"rec-{written.Id.Wire}", asked.SessionId.Value);
        Assert.Equal(written.Id.Wire, asked.RecordingId);
        Assert.Equal("primary", asked.OutputRoot);
        Assert.Equal(new DateTimeOffset(Airs.AddMinutes(30)), asked.EndsAt);
        Assert.Equal(TuneSystem.IsdbT, asked.Tune!.System);
        Assert.Equal(27, asked.Tune.IsdbT!.PhysicalChannel);
        Assert.Empty(asked.Validate(new DateTimeOffset(Airs)));
    }

    [Fact]
    public async Task TheRowTheTickWritesHoldsTheWindowThePromiseLeavesForTheRecording()
    {
        var recordings = new HeldRecordings();

        await Round(Holding(Due(1)), recordings, new RecordingDriver()).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(new DateTime(2026, 8, 26, 20, 0, 15, DateTimeKind.Utc), written.ExpectedWindowStart);
        Assert.Equal(new DateTime(2026, 8, 26, 20, 30, 0, DateTimeKind.Utc), written.ExpectedWindowEnd);
        Assert.Equal(Airs, written.StartedAtActual);
    }

    [Fact]
    public async Task WhatTheReservationKnewAboutTheProgrammeIsCopiedIntoTheRecording()
    {
        var recordings = new HeldRecordings();
        RecordingTick due = Due(1);

        await Round(Holding(due), recordings, new RecordingDriver()).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(due.Id, written.ReservationId);
        Assert.Equal(due.Programme, written.Programme);
        Assert.Equal("A programme", written.SnapshotName);
        Assert.Equal("What it is about", written.SnapshotSummary);
        Assert.Equal("Every detail of it", written.SnapshotExtended);
        Assert.Equal(new DateTime(2026, 8, 26, 14, 0, 0, DateTimeKind.Utc), written.CapturedAt);
        Assert.Equal(BroadcastGroupRole.Standalone, written.BroadcastGroupRole);
        Assert.Equal("adapter0", written.TunerDeviceId!.Value);
        Assert.Equal($"{written.Id.Wire}.ts", written.FileName.Value);
        Assert.Equal("primary", written.OutputRoot.Value);
    }

    [Fact]
    public async Task ADriverThatNamedNoTunerStillHasItsRecordingWrittenDown()
    {
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver { DeviceId = string.Empty };

        await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Null(Assert.Single(recordings.Rows).TunerDeviceId);
    }

    [Fact]
    public async Task TheDiskIsWeighedBeforeTheSessionIsAskedFor()
    {
        var driver = new RecordingDriver();

        await Round(Holding(Due(1)), new HeldRecordings(), driver).RunAsync(CancellationToken.None);

        Assert.Equal(2, driver.Log.Count);
        Assert.Equal("storage", driver.Log[0]);
        Assert.StartsWith("start:", driver.Log[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARootWithNoRoomLeftIsWrittenDownAndTheRecordingStartsAnyway()
    {
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver { FreeBytes = 0 };

        RecordingRun run = await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);
        OutcomeDetail reason = Assert.Single(written.OutcomeDetail);

        Assert.Single(run.Started);
        Assert.Equal(RecordingFault.RefusedByDiskPrecheck, reason.Fault);
        Assert.Equal(string.Empty, reason.Note);
        Assert.Equal(Airs, reason.NoticedAt);
    }

    [Fact]
    public async Task ARootWithRoomLeavesNothingToWriteDown()
    {
        var recordings = new HeldRecordings();

        await Round(Holding(Due(1)), recordings, new RecordingDriver()).RunAsync(CancellationToken.None);

        Assert.Empty(Assert.Single(recordings.Rows).OutcomeDetail);
    }

    [Fact]
    public async Task WhatIsAlreadyRunningIsWeighedBesideWhatIsStarting()
    {
        IReadOnlyList<Recording> running = [InFlight(Airs.AddMinutes(-10), Airs.AddMinutes(20))];

        Assert.Empty(await Weighing([], free: RoomForTheOneStarting));
        Assert.Single(await Weighing(running, free: RoomForTheOneStarting));
        Assert.Empty(await Weighing(running, free: RoomForItAndTheOneAlreadyRunning));
    }

    [Fact]
    public async Task TheRoomAskedForIsTheRoomTheEstimateNeedsAndNoMore()
    {
        var running = new List<Recording> { InFlight(Airs.AddMinutes(-10), Airs.AddMinutes(20)) };

        Assert.Empty(await Weighing(running, free: 6_156_562_500L));
        Assert.Single(await Weighing(running, free: 6_156_562_499L));
        Assert.Single(await Weighing(running, free: 5_511_562_500L));
    }

    [Fact]
    public async Task ARecordingThisTickStoppedIsNoLongerOneThatIsRunning()
    {
        Assert.Empty(await Instead(Airs));
        Assert.Single(await Instead(Airs.AddTicks(1)));
    }

    [Fact]
    public async Task ARecordingThisTickStoppedLeavesTheDiskRoomForTheNextOne()
    {
        IReadOnlyList<Recording> stopping = [InFlight(Airs.AddMinutes(-30), Airs)];

        Assert.Empty(await Weighing(stopping, free: RoomForTheOneStarting));
        Assert.Single(await Weighing(stopping, free: RoomForTheOneStarting - 1));
    }

    [Fact]
    public async Task AReservationAlreadyInFlightIsNotStartedAgain()
    {
        PlannedReservations reservations = Holding(Due(1, startedAt: Airs.AddMinutes(-5)));
        var driver = new RecordingDriver();

        RecordingRun run = await Round(reservations, new HeldRecordings(), driver)
            .RunAsync(CancellationToken.None);

        Assert.Empty(run.Started);
        Assert.Empty(run.Refused);
        Assert.Empty(reservations.Claimed);
        Assert.Empty(driver.Log);
    }

    [Fact]
    public async Task EachReservationThatIsDueGetsItsOwnRecording()
    {
        var recordings = new HeldRecordings();

        RecordingRun run = await Round(Holding(Due(1), Due(2)), recordings, new RecordingDriver())
            .RunAsync(CancellationToken.None);

        Assert.Equal(2, run.Started.Count);
        Assert.Equal(2, recordings.Rows.Count);
        Assert.Equal(2, recordings.Rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(2, recordings.Rows.Select(row => row.FileName).Distinct().Count());
    }

    [Fact]
    public async Task ALedgerThatWillNotTakeTheRowStopsTheSessionAndGivesTheClaimBack()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
        };
        var driver = new RecordingDriver();

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(reservations.Claimed));
        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(recordings.Rows);
        Assert.Empty(run.Started);
        Assert.Equal(RecordingRefusalKind.StartAbandoned, Assert.Single(run.Refused).Kind);

        StartSessionRequest asked = Assert.Single(driver.Started);

        Assert.Equal(asked.SessionId, Assert.Single(driver.Stopped));
        Assert.Equal(
            "the ledger would not take the row this session belongs to",
            Assert.Single(driver.StopReasons));
    }

    [Fact]
    public async Task AnythingThatThrowsBeforeTheDriverWasAskedGivesTheClaimBackWithoutStoppingAnything()
    {
        RecordingTick due = Due(1, until: Airs.AddMinutes(30));
        PlannedReservations reservations = Holding(due);
        var driver = new RecordingDriver { RefusingStorage = new InvalidOperationException("no storage") };

        RecordingRun run = await Round(reservations, new HeldRecordings(), driver).RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(driver.Started);
        Assert.Empty(driver.Stopped);
        Assert.Equal(RecordingRefusalKind.StartAbandoned, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task OneReservationThatCouldNotBeStartedDoesNotStopTheNextOne()
    {
        RecordingTick first = Due(1);
        RecordingTick second = Due(2);
        PlannedReservations reservations = Holding(first, second);
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
            RefusingToAddOnce = true,
        };
        var driver = new RecordingDriver();

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Single(run.Started);
        Assert.Equal(second.Id, Assert.Single(recordings.Rows).ReservationId);
        Assert.Equal(RecordingRefusalKind.StartAbandoned, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task ADriverThatDidNotAnswerIsAskedWhetherTheSessionStandsBeforeTheClaimIsGivenBack()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Unreachable("the answer never came"),
            HoldsWhatItStarted = true,
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(Assert.Single(driver.Started).SessionId, Assert.Single(driver.Asked));
        Assert.Empty(reservations.Released);
        Assert.Equal(written.Id, Assert.Single(run.Started));
        Assert.Empty(run.Unconfirmed);
        Assert.Equal("adapter0", written.TunerDeviceId!.Value);
    }

    [Fact]
    public async Task ADriverThatSaysItHoldsNoSuchSessionGetsTheClaimBack()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Unreachable("the answer never came"),
            HoldsWhatItStarted = false,
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(recordings.Rows);
        Assert.Equal(RecordingRefusalKind.DriverUnreachable, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task ASessionNobodyCanAskAboutIsWrittenDownRatherThanLeftForASecondAttempt()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Unreachable("the answer never came"),
            AnswersWhenAsked = DriverCall<SessionSnapshot>.Unreachable("still nothing"),
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Empty(reservations.Released);
        Assert.Equal(written.Id, Assert.Single(run.Started));
        Assert.Equal(written.Id, Assert.Single(run.Unconfirmed));
        Assert.Null(written.TunerDeviceId);
        Assert.Equal($"{written.Id.Wire}.ts", written.FileName.Value);
    }

    [Fact]
    public async Task ARefusedStartIsNotAskedAboutTwice()
    {
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.NoDeviceFree, [])),
        };

        await Round(Holding(Due(1)), new HeldRecordings(), driver).RunAsync(CancellationToken.None);

        Assert.Empty(driver.Asked);
    }

    [Fact]
    public async Task ARecordingStartedThisTickIsWeighedAgainstTheDiskForTheNextOne()
    {
        IReadOnlyList<IReadOnlyList<OutcomeDetail>> forOne = await StartingTwo(RoomForTheOneStarting);

        Assert.Empty(forOne[0]);
        Assert.Equal(
            RecordingFault.RefusedByDiskPrecheck,
            Assert.Single(forOne[1]).Fault);

        IReadOnlyList<IReadOnlyList<OutcomeDetail>> forBoth = await StartingTwo(RoomForTheOneStarting * 2);

        Assert.Empty(forBoth[0]);
        Assert.Empty(forBoth[1]);
    }

    [Fact]
    public async Task AStopThatNeverLandedKeepsTheClaimRatherThanGuessingNothingStands()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
        };
        var driver = new RecordingDriver
        {
            RefusesToStop = DriverCall<SessionSnapshot>.Unreachable("the socket went away too"),
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Single(driver.Stopped);
        Assert.Empty(reservations.Released);
        Assert.Single(run.Unconfirmed);
        Assert.Equal(RecordingRefusalKind.StartAbandoned, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task AnUndoThatItselfThrowsLeavesTheClaimHeldAndTheTickRunning()
    {
        RecordingTick first = Due(1);
        RecordingTick second = Due(2);
        PlannedReservations reservations = Holding(first, second);
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
            RefusingToAddOnce = true,
        };
        var driver = new RecordingDriver
        {
            ThrowingOnStop = new InvalidOperationException("the stop went nowhere either"),
        };

        RecordingRun run = await Round(reservations, recordings, driver).RunAsync(CancellationToken.None);

        Assert.Empty(reservations.Released);
        Assert.Single(run.Unconfirmed);
        Assert.Single(run.Started);
        Assert.Equal(second.Id, Assert.Single(recordings.Rows).ReservationId);
    }

    [Fact]
    public async Task ASessionTheStartCallThrewOverIsStillStopped()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var driver = new RecordingDriver
        {
            ThrowingOnStart = new InvalidOperationException("the request came apart"),
        };

        RecordingRun run = await Round(reservations, new HeldRecordings(), driver).RunAsync(CancellationToken.None);

        Assert.Equal(Assert.Single(driver.Started).SessionId, Assert.Single(driver.Stopped));
        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(run.Unconfirmed);
        Assert.Equal(RecordingRefusalKind.StartAbandoned, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task ATickThatWasCancelledSaysSoRatherThanCallingItARefusal()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var driver = new RecordingDriver
        {
            ThrowingOnStart = new OperationCanceledException("the host is going down"),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Round(reservations, new HeldRecordings(), driver).RunAsync(CancellationToken.None));

        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task GivingAClaimBackNeverRidesOnTheTokenTheTickWasGiven()
    {
        PlannedReservations reservations = Holding(Due(1));
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.UnknownOutputRoot, [])),
        };
        using var cancellable = new CancellationTokenSource();

        RecordingRun run = await Round(reservations, new HeldRecordings(), driver).RunAsync(cancellable.Token);

        Assert.True(cancellable.Token.CanBeCanceled);
        Assert.False(Assert.Single(reservations.ReleaseTokens).CanBeCanceled);
        Assert.Equal(RecordingRefusalKind.DriverRefused, Assert.Single(run.Refused).Kind);
    }

    [Fact]
    public async Task NothingTheUndoDoesRidesOnTheTokenTheTickWasGiven()
    {
        PlannedReservations reservations = Holding(Due(1));
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
        };
        var driver = new RecordingDriver();
        using var cancellable = new CancellationTokenSource();

        await Round(reservations, recordings, driver).RunAsync(cancellable.Token);

        Assert.False(Assert.Single(driver.StopTokens).CanBeCanceled);
        Assert.False(Assert.Single(reservations.ReleaseTokens).CanBeCanceled);
    }

    [Fact]
    public async Task TheSoundIsTakenFromWhatTheGuideAnnouncesWhenTheRecordingStarts()
    {
        RecordingTick due = Due(1);
        var recordings = new HeldRecordings();

        await Round(
                Holding(due),
                recordings,
                new RecordingDriver(),
                programmes: Announcing(due, AudioMode.DualMono, 2))
            .RunAsync(CancellationToken.None);

        Assert.Equal(AudioMode.Undetermined, due.Snapshot.Audio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, due.Snapshot.Sounds);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(AudioMode.DualMono, written.SnapshotAudio);
        Assert.Equal(2, written.SnapshotSounds);
    }

    [Fact]
    public async Task TheSoundTheReservationCarriesStandsWhenTheGuideNoLongerHoldsTheProgramme()
    {
        RecordingTick due = Due(1, audio: AudioMode.DualMono, sounds: 2);
        var recordings = new HeldRecordings();

        await Round(Holding(due), recordings, new RecordingDriver()).RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(AudioMode.DualMono, written.SnapshotAudio);
        Assert.Equal(2, written.SnapshotSounds);
    }

    [Fact]
    public async Task AnAnnouncementThatSaysNothingOfTheSoundLeavesTheReservationsWordStanding()
    {
        RecordingTick due = Due(1, audio: AudioMode.DualMono, sounds: 2);
        var recordings = new HeldRecordings();

        await Round(
                Holding(due),
                recordings,
                new RecordingDriver(),
                programmes: Announcing(due, AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced))
            .RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(AudioMode.DualMono, written.SnapshotAudio);
        Assert.Equal(2, written.SnapshotSounds);
    }

    [Fact]
    public async Task NothingElseInTheSnapshotIsReReadWhenTheRecordingStarts()
    {
        RecordingTick due = Due(1);
        var recordings = new HeldRecordings();

        await Round(
                Holding(due),
                recordings,
                new RecordingDriver(),
                programmes: Announcing(due, AudioMode.Stereo, 1))
            .RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(due.Snapshot.Name, written.SnapshotName);
        Assert.Equal(due.Snapshot.Summary, written.SnapshotSummary);
        Assert.Equal(due.Snapshot.Extended, written.SnapshotExtended);
        Assert.Equal(due.Snapshot.Genres, written.SnapshotGenres);
        Assert.Equal(due.Snapshot.CapturedAt, written.CapturedAt);
    }

    private static async Task<IReadOnlyList<OutcomeDetail>> Weighing(IReadOnlyList<Recording> running, long free)
    {
        var recordings = new HeldRecordings();
        recordings.Rows.AddRange(running);

        await Round(Holding(Due(1)), recordings, new RecordingDriver { FreeBytes = free })
            .RunAsync(CancellationToken.None);

        return recordings.Rows[^1].OutcomeDetail;
    }

    private static async Task<IReadOnlyList<Guid>> Instead(DateTime windowEnd)
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), windowEnd));

        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.NoDeviceFree, [])),
        };

        RecordingRun run = await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        return Assert.Single(run.Refused).RecordedInstead;
    }

    private static async Task<IReadOnlyList<IReadOnlyList<OutcomeDetail>>> StartingTwo(long free)
    {
        var recordings = new HeldRecordings();

        await Round(Holding(Due(1), Due(2)), recordings, new RecordingDriver { FreeBytes = free })
            .RunAsync(CancellationToken.None);

        Assert.Equal(2, recordings.Rows.Count);

        return [.. recordings.Rows.Select(row => row.OutcomeDetail)];
    }

    private static async Task<IReadOnlyList<RecordingId>> Stopping(DateTime windowEnd)
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), windowEnd));

        RecordingRun run = await Round(Holding(), recordings, new RecordingDriver())
            .RunAsync(CancellationToken.None);

        return run.Stopped;
    }

    [Fact]
    public async Task ARecordingStartedLateIsPromisedTheWindowItsReservationAskedFor()
    {
        var recordings = new HeldRecordings();
        DateTime fiftyMinutesLate = Airs.AddMinutes(50);

        await Round(
                Holding(Due(1, until: Airs.AddMinutes(200))),
                recordings,
                new RecordingDriver(),
                at: fiftyMinutesLate)
            .RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(Airs + Head, written.ExpectedWindowStart);
        Assert.Equal(Airs.AddMinutes(200), written.ExpectedWindowEnd);
        Assert.Equal(fiftyMinutesLate, written.StartedAtActual);
    }

    [Fact]
    public async Task AReservationThatOpensPartWayThroughIsPromisedFromWhereItOpensRatherThanFromTheRound()
    {
        var recordings = new HeldRecordings();
        DateTime opens = Airs.AddMinutes(50);

        await Round(
                Holding(Due(1, from: opens, until: Airs.AddMinutes(200))),
                recordings,
                new RecordingDriver(),
                at: opens.AddSeconds(4))
            .RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(opens + Head, written.ExpectedWindowStart);
    }

    [Fact]
    public async Task WhatIsDueIsAskedForAsFarAheadAsTheRecorderPrepares()
    {
        PlannedReservations reservations = Holding(Due(1));

        await Round(reservations, new HeldRecordings(), new RecordingDriver()).RunAsync(CancellationToken.None);

        Assert.Equal(Settings.StartingAhead, Assert.Single(reservations.AskedAhead));
        Assert.Equal(TimeSpan.FromSeconds(10), Settings.StartingAhead);
    }

    [Fact]
    public async Task AReservationAboutToOpenIsStartedBeforeItsStartAndKeepsTheWindowItWasPromised()
    {
        var recordings = new HeldRecordings();
        var driver = new RecordingDriver();
        DateTime aheadOfIt = Airs.AddSeconds(-8);

        RecordingRun run = await Round(Holding(Due(1)), recordings, driver, at: aheadOfIt)
            .RunAsync(CancellationToken.None);

        Recording written = Assert.Single(recordings.Rows);

        Assert.Equal(written.Id, Assert.Single(run.Started));
        Assert.Single(driver.Started);
        Assert.Equal(aheadOfIt, written.StartedAtActual);
        Assert.Equal(Airs + Head, written.ExpectedWindowStart);
        Assert.Empty(run.HeldBack);
    }

    [Theory]
    [InlineData("noDeviceFree")]
    [InlineData("noLock")]
    [InlineData("draining")]
    public async Task ARefusalAheadOfTheStartGivesTheClaimBackAndWritesNothingDown(string title)
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(new DriverProblem(title, [])),
        };

        RecordingRun run = await Round(
                reservations,
                new HeldRecordings(),
                driver,
                at: Airs.AddSeconds(-5),
                ledger: ledger)
            .RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(run.HeldBack));
        Assert.Empty(run.Refused);
        Assert.Empty(run.Started);
        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Empty(ledger.Outcomes.Held);
        Assert.Empty(ledger.Tuning.Failures);
    }

    [Fact]
    public async Task TheSameRefusalOnceTheStartHasComeIsWrittenDown()
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.NoDeviceFree, [])),
        };

        RecordingRun run = await Round(Holding(due), new HeldRecordings(), driver, ledger: ledger)
            .RunAsync(CancellationToken.None);

        Assert.Empty(run.HeldBack);
        Assert.Equal(RecordingRefusalKind.TunerContended, Assert.Single(run.Refused).Kind);
        Assert.Single(ledger.Outcomes.Held);
    }

    [Fact]
    public async Task AChannelThatCannotBeTunedAheadOfTheStartIsLeftForTheStartToReport()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);

        RecordingRun run = await Round(
                reservations,
                new HeldRecordings(),
                new RecordingDriver(),
                resolution: TuningResolution.Refused(TuningRefusal.NoSelectedChannel),
                at: Airs.AddSeconds(-5))
            .RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(run.HeldBack));
        Assert.Empty(run.Refused);
        Assert.Empty(reservations.Claimed);
    }

    [Fact]
    public async Task AStartThatThrowsAheadOfItsStartStopsTheSessionGivesTheClaimBackAndIsTriedAgain()
    {
        RecordingTick due = Due(1);
        PlannedReservations reservations = Holding(due);
        var recordings = new HeldRecordings
        {
            RefusingToAdd = new InvalidOperationException("the ledger would not take it"),
        };
        var driver = new RecordingDriver();

        RecordingRun run = await Round(reservations, recordings, driver, at: Airs.AddSeconds(-5))
            .RunAsync(CancellationToken.None);

        Assert.Equal(due.Id, Assert.Single(run.HeldBack));
        Assert.Empty(run.Refused);
        Assert.Equal(due.Id, Assert.Single(reservations.Released));
        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task AGuideThatCannotBeReadLeavesTheRestOfTheRoundStanding()
    {
        var recordings = new HeldRecordings();
        ReservationId following = ReservationId.New();
        Recording over = InFlight(Airs.AddMinutes(-30), Airs);
        Recording running = InFlight(Airs.AddMinutes(-30), Airs.AddMinutes(30), reservationId: following);

        recordings.Rows.Add(over);
        recordings.Rows.Add(running);

        var driver = new RecordingDriver();
        RecordingRun run = await Round(
                Holding(Due(9, startedAt: Airs) with { Id = following }),
                recordings,
                driver,
                programmes: new UnreadableGuide())
            .RunAsync(CancellationToken.None);

        Assert.Equal(over.Id, Assert.Single(run.Stopped));
        Assert.Empty(run.Followed);
    }

    private static PlannedReservations Holding(params RecordingTick[] ticks)
        => new PlannedReservations().Holding(ticks);

    private static RecordingRound Round(
        PlannedReservations reservations,
        HeldRecordings recordings,
        RecordingDriver driver,
        TuningResolution? resolution = null,
        DateTime? at = null,
        RefusalLedger? ledger = null,
        IAnnouncedProgrammes? programmes = null)
    {
        var clock = new HeldMoment(at ?? Airs);
        IAnnouncedProgrammes held = programmes ?? new HeldProgrammes();
        var disks = new DiskPrecheckService(new StorageMonitor(driver, clock, StorageMonitorSettings.Default));
        RefusalLedger kept = ledger ?? new RefusalLedger();

        return new RecordingRound(
            reservations,
            recordings,
            held,
            new ResolvedTuning(resolution ?? Terrestrial),
            disks,
            driver,
            new ProgramExtensionFollower(
                recordings,
                held,
                driver,
                new EndsAlreadyAsked(),
                Settings,
                NullLogger<ProgramExtensionFollower>.Instance),
            kept.Reporter,
            kept.Retries(held, disks, Settings),
            Settings,
            clock);
    }

    [Fact(DisplayName = "a start that did not lock is started again once the pause has passed, while the programme is on")]
    public async Task AStartThatDidNotLockIsStartedAgainOnceThePauseHasPassedWhileTheProgrammeIsOn()
    {
        var scene = new Scene(Due(1));
        scene.Driver.RefusesToStart = WouldNotLock;

        await scene.TickAsync(Airs);

        scene.Driver.RefusesToStart = null;

        RecordingRun run = await scene.TickAsync(Airs + Policy.BetweenAttempts);

        Recording written = Assert.Single(scene.Recordings.Rows);
        ReservationOutcome retried = Assert.Single(scene.Lines(ReservationOutcomeKind.Retried));

        Assert.Equal(written.Id, Assert.Single(run.Started));
        Assert.Equal([scene.Due.Id], run.Retried);
        Assert.Equal(2, scene.Driver.Started.Count);
        Assert.Equal(scene.Due.Id, written.ReservationId);
        Assert.Equal(RetryResult.Started, retried.RetryResult);
        Assert.Empty(retried.Faults);
        Assert.Equal(Airs + Policy.BetweenAttempts, retried.OccurredAt);
        Assert.Single(scene.Lines(ReservationOutcomeKind.TuneFailure));
        Assert.Empty(scene.Lines(ReservationOutcomeKind.GaveUpRetrying));
    }

    [Fact(DisplayName = "nothing is started again before the pause, and no claim is taken while waiting")]
    public async Task NothingIsStartedAgainBeforeThePauseAndNoClaimIsTakenWhileWaiting()
    {
        var scene = new Scene(Due(1));
        scene.Driver.RefusesToStart = WouldNotLock;

        await scene.TickAsync(Airs);

        scene.Driver.RefusesToStart = null;

        RecordingRun run = await scene.TickAsync(Airs + Policy.BetweenAttempts - TimeSpan.FromTicks(1));

        Assert.Empty(run.Started);
        Assert.Empty(run.Retried);
        Assert.Empty(run.GaveUp);
        Assert.Single(scene.Driver.Started);
        Assert.Single(scene.Reservations.Claimed);
        Assert.Empty(scene.Lines(ReservationOutcomeKind.Retried));
    }

    [Fact(DisplayName = "each attempt refused again is written down with its class, and the ceiling holds")]
    public async Task EachAttemptRefusedAgainIsWrittenDownWithItsClassAndTheCeilingHolds()
    {
        var scene = new Scene(Due(1));
        scene.Driver.RefusesToStart = WouldNotLock;

        await scene.TickAsync(Airs);
        await scene.TickAsync(Airs.AddMinutes(1));
        await scene.TickAsync(Airs.AddMinutes(2));

        RecordingRun spent = await scene.TickAsync(Airs.AddMinutes(3));
        RecordingRun after = await scene.TickAsync(Airs.AddMinutes(4));

        ReservationOutcome[] retried = [.. scene.Lines(ReservationOutcomeKind.Retried).OrderBy(line => line.OccurredAt)];
        ReservationOutcome gaveUp = Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying));

        Assert.Equal(3, scene.Driver.Started.Count);
        Assert.Equal(3, scene.Ledger.Tuning.Failures.Count);
        Assert.Equal([Airs.AddMinutes(1), Airs.AddMinutes(2)], retried.Select(line => line.OccurredAt));
        Assert.All(retried, line =>
        {
            Assert.Equal(RetryResult.RefusedAgain, line.RetryResult);
            Assert.Equal(TuneFailureKind.NoLock, line.TuneFailure);
            Assert.Equal([RecordingFault.TuneFailed], line.Faults);
        });
        Assert.Equal(RetryGiveUp.AttemptsSpent, gaveUp.GaveUpBecause);
        Assert.Equal(Airs.AddMinutes(3), gaveUp.OccurredAt);
        Assert.Equal([scene.Due.Id], spent.GaveUp);
        Assert.Empty(after.GaveUp);
        Assert.Empty(after.Refused);
    }

    [Fact(DisplayName = "a stream that is not the one expected is never started again, and giving up is written once")]
    public async Task AStreamThatIsNotTheOneExpectedIsNeverStartedAgainAndGivingUpIsWrittenOnce()
    {
        var scene = new Scene(Due(1));
        scene.FailedEarlier(TuneFailureKind.StreamMismatch, Airs);

        RecordingRun first = await scene.TickAsync(Airs.AddMinutes(2));
        RecordingRun second = await scene.TickAsync(Airs.AddMinutes(3));

        ReservationOutcome gaveUp = Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying));

        Assert.Empty(scene.Driver.Started);
        Assert.Empty(scene.Reservations.Claimed);
        Assert.Equal(RetryGiveUp.NotTransient, gaveUp.GaveUpBecause);
        Assert.Equal(TuneFailureKind.StreamMismatch, gaveUp.TuneFailure);
        Assert.Equal([scene.Due.Id], first.GaveUp);
        Assert.Empty(second.GaveUp);
        Assert.Empty(scene.Lines(ReservationOutcomeKind.Retried));
    }

    [Fact(DisplayName = "a programme that has ended is not started again, though the margin after it is still open")]
    public async Task AProgrammeThatHasEndedIsNotStartedAgainThoughTheMarginAfterItIsStillOpen()
    {
        var scene = new Scene(Due(1, until: Airs.AddMinutes(30), marginAfter: TimeSpan.FromMinutes(5)));
        scene.FailedEarlier(TuneFailureKind.NoLock, Airs.AddMinutes(20));

        RecordingRun run = await scene.TickAsync(Airs.AddMinutes(26));

        Assert.Empty(scene.Driver.Started);
        Assert.Empty(run.Started);
        Assert.Equal(
            RetryGiveUp.BroadcastOver,
            Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying)).GaveUpBecause);
    }

    [Fact(DisplayName = "a programme the guide has withdrawn is not started again")]
    public async Task AProgrammeTheGuideHasWithdrawnIsNotStartedAgain()
    {
        var scene = new Scene(Due(1));
        Programme another = Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(scene.Due.NetworkId, scene.Due.ServiceId, new EventId(2)),
                new TransportStreamId(32736),
                Airs.AddMinutes(30),
                Airs.AddMinutes(60),
                "The next programme",
                string.Empty,
                false),
            Airs.AddHours(-3));
        another.Heard(Airs.AddMinutes(1));
        scene.Programmes.Programmes.Add(another);
        scene.FailedEarlier(TuneFailureKind.NoLock, Airs);

        await scene.TickAsync(Airs.AddMinutes(2));

        Assert.Empty(scene.Driver.Started);
        Assert.Equal(
            RetryGiveUp.BroadcastOver,
            Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying)).GaveUpBecause);
    }

    [Fact(DisplayName = "a channel already set aside as needing attention is not pulled back")]
    public async Task AChannelAlreadySetAsideAsNeedingAttentionIsNotPulledBack()
    {
        CandidateChannel candidate = Candidate();

        for (int failure = 0; failure < RotationBackoff.Default.FailureCeiling; failure++)
        {
            candidate.RecordTuningFailure(RotationBackoff.Default, Airs);
        }

        var scene = new Scene(Due(1), candidate);
        scene.FailedEarlier(TuneFailureKind.NoLock, Airs);

        await scene.TickAsync(Airs.AddMinutes(2));

        Assert.False(candidate.IsInRotation);
        Assert.Empty(scene.Driver.Started);
        Assert.Equal(
            RetryGiveUp.CandidateNeedsAttention,
            Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying)).GaveUpBecause);
    }

    [Fact(DisplayName = "a channel that is backing off is waited for past the pause")]
    public async Task AChannelThatIsBackingOffIsWaitedForPastThePause()
    {
        CandidateChannel candidate = Candidate();
        candidate.RecordTuningFailure(RotationBackoff.Default, Airs);
        candidate.RecordTuningFailure(RotationBackoff.Default, Airs);

        var scene = new Scene(Due(1), candidate);
        scene.FailedEarlier(TuneFailureKind.NoLock, Airs);

        RecordingRun resting = await scene.TickAsync(candidate.NextAttemptAt!.Value - TimeSpan.FromTicks(1));
        RecordingRun rested = await scene.TickAsync(candidate.NextAttemptAt!.Value);

        Assert.True(candidate.NextAttemptAt > Airs + Policy.BetweenAttempts);
        Assert.Empty(resting.Started);
        Assert.Single(rested.Started);
        Assert.Single(scene.Driver.Started);
    }

    [Fact(DisplayName = "a start the disk precheck finds no room for is not tried again")]
    public async Task AStartTheDiskPrecheckFindsNoRoomForIsNotTriedAgain()
    {
        var scene = new Scene(Due(1));
        scene.Driver.FreeBytes = 0;
        scene.FailedEarlier(TuneFailureKind.NoData, Airs);

        await scene.TickAsync(Airs.AddMinutes(2));

        ReservationOutcome gaveUp = Assert.Single(scene.Lines(ReservationOutcomeKind.GaveUpRetrying));

        Assert.DoesNotContain(scene.Driver.Log, entry => entry.StartsWith("start:", StringComparison.Ordinal));
        Assert.Equal(RetryGiveUp.PrecheckFailed, gaveUp.GaveUpBecause);
        Assert.Equal([RecordingFault.RefusedByDiskPrecheck], gaveUp.Faults);
    }

    [Fact]
    public async Task AFirstStartIsHeldBackByNoneOfThis()
    {
        var scene = new Scene(Due(1));
        scene.Driver.FreeBytes = 0;

        RecordingRun run = await scene.TickAsync(Airs);

        Assert.Single(run.Started);
        Assert.Empty(run.Retried);
        Assert.Empty(scene.Ledger.Outcomes.Held);
    }

    [Fact]
    public async Task AStartRefusedForWantOfATunerIsLeftToTheTickAsItAlwaysWas()
    {
        var scene = new Scene(Due(1));
        scene.Driver.RefusesToStart = DriverCall<SessionSnapshot>.Refused(
            new DriverProblem(SessionRefusalTitles.NoDeviceFree, []));

        await scene.TickAsync(Airs);

        scene.Driver.RefusesToStart = null;

        RecordingRun run = await scene.TickAsync(Airs.AddSeconds(5));

        Assert.Single(run.Started);
        Assert.Empty(run.Retried);
        Assert.Empty(scene.Lines(ReservationOutcomeKind.Retried));
        Assert.Empty(scene.Lines(ReservationOutcomeKind.GaveUpRetrying));
    }

    private static CandidateChannel Candidate()
        => CandidateChannel.Discover(
            new CandidateChannelId(Guid.NewGuid()),
            new NetworkId(32736),
            new ServiceId(1024),
            TuningParameters.Terrestrial(27),
            Airs.AddDays(-1));

    private sealed class Scene
    {
        private readonly TuningResolution resolution;

        public Scene(RecordingTick due, CandidateChannel? candidate = null)
        {
            Due = due;
            Ledger = new RefusalLedger().Knowing(due);
            Reservations = new PlannedReservations().Holding(due);

            if (candidate is not null)
            {
                Ledger.Candidates.Candidates.Add(candidate);
            }

            resolution = candidate is null
                ? Terrestrial
                : TuningResolution.Tunable(candidate.Id, candidate.Tuning, impaired: false);
        }

        public RecordingTick Due { get; }

        public RefusalLedger Ledger { get; }

        public PlannedReservations Reservations { get; }

        public HeldRecordings Recordings { get; } = new();

        public RecordingDriver Driver { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public IEnumerable<ReservationOutcome> Lines(ReservationOutcomeKind kind)
            => Ledger.Outcomes.Held.Where(line => line.Kind == kind);

        public void FailedEarlier(TuneFailureKind kind, DateTime at)
            => Ledger.Outcomes.Standing(ReservationOutcome.Record(
                ReservationOutcomeId.New(),
                Ledger.Reservations.Held.Single(),
                ReservationOutcomeKind.TuneFailure,
                kind,
                null,
                [RecordingFault.TuneFailed],
                [],
                at));

        public Task<RecordingRun> TickAsync(DateTime at)
        {
            var clock = new HeldMoment(at);
            var disks = new DiskPrecheckService(new StorageMonitor(Driver, clock, StorageMonitorSettings.Default));

            return new RecordingRound(
                Reservations,
                Recordings,
                Programmes,
                new ResolvedTuning(resolution),
                disks,
                Driver,
                new ProgramExtensionFollower(
                    Recordings,
                    Programmes,
                    Driver,
                    new EndsAlreadyAsked(),
                    Settings,
                    NullLogger<ProgramExtensionFollower>.Instance),
                Ledger.Reporter,
                Ledger.Retries(Programmes, disks, Settings, Policy),
                Settings,
                clock).RunAsync(CancellationToken.None);
        }
    }
}
