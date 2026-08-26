using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Recordings.RecordingTickFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingRoundTests
{
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

        Assert.Equal(4, refusals.Length);
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
        Assert.Contains("NoRoomLeft", reason.Note, StringComparison.Ordinal);
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
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-10), Airs.AddMinutes(20)));

        var driver = new RecordingDriver { FreeBytes = 1 };

        await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        Recording written = recordings.Rows[^1];

        Assert.Contains("2 recordings weigh 6156562500 bytes", Assert.Single(written.OutcomeDetail).Note, StringComparison.Ordinal);
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
    public async Task ARecordingThisTickStoppedIsNoLongerWeighedAgainstTheDisk()
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), Airs));

        var driver = new RecordingDriver { FreeBytes = 1 };

        await Round(Holding(Due(1)), recordings, driver).RunAsync(CancellationToken.None);

        Assert.Contains(
            "1 recordings weigh 3681562500 bytes",
            Assert.Single(recordings.Rows[^1].OutcomeDetail).Note,
            StringComparison.Ordinal);
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

    private static async Task<IReadOnlyList<OutcomeDetail>> Weighing(IReadOnlyList<Recording> running, long free)
    {
        var recordings = new HeldRecordings();
        recordings.Rows.AddRange(running);

        await Round(Holding(Due(1)), recordings, new RecordingDriver { FreeBytes = free })
            .RunAsync(CancellationToken.None);

        return recordings.Rows[^1].OutcomeDetail;
    }

    private static async Task<IReadOnlyList<RecordingId>> Stopping(DateTime windowEnd)
    {
        var recordings = new HeldRecordings();
        recordings.Rows.Add(InFlight(Airs.AddMinutes(-30), windowEnd));

        RecordingRun run = await Round(Holding(), recordings, new RecordingDriver())
            .RunAsync(CancellationToken.None);

        return run.Stopped;
    }

    private static PlannedReservations Holding(params RecordingTick[] ticks)
        => new PlannedReservations().Holding(ticks);

    private static RecordingRound Round(
        PlannedReservations reservations,
        HeldRecordings recordings,
        RecordingDriver driver,
        TuningResolution? resolution = null)
    {
        var clock = new HeldMoment(Airs);

        return new RecordingRound(
            reservations,
            recordings,
            new ResolvedTuning(resolution ?? Terrestrial),
            new DiskPrecheckService(new StorageMonitor(driver, clock, StorageMonitorSettings.Default)),
            driver,
            Settings,
            clock);
    }
}
