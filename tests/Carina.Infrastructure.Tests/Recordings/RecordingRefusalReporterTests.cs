using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Recordings.RecordingTickFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingRefusalReporterTests
{
    public static TheoryData<string> WhatTheLedgerHasNoClassFor =>
        [
            SessionRefusalTitles.DeviceUnavailable,
            SessionRefusalTitles.Draining,
            SessionRefusalTitles.FaultedDevice,
            SessionRefusalTitles.UnknownOutputRoot,
            SessionRefusalTitles.DuplicateSession,
        ];

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, TuneFailureKind.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, TuneFailureKind.NoData)]
    public async Task ATunerThatCouldNotReceiveIsWrittenDownAsOneOfTheFourAndToldToTheRotation(
        string title,
        TuneFailureKind kind)
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);

        await RunAsync(due, ledger, title);

        ReservationOutcome written = Assert.Single(ledger.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.TuneFailure, written.Kind);
        Assert.Equal(kind, written.TuneFailure);
        Assert.Equal([RecordingFault.TuneFailed], written.Faults);
        Assert.Null(written.RecordingOutcome);
        Assert.Equal(due.Id, written.ReservationId);
        Assert.Equal(Airs, written.OccurredAt);
        Assert.Equal(Terrestrial.CandidateChannelId, Assert.Single(ledger.Tuning.Failures));
    }

    [Fact]
    public async Task ALedgerRowCarriesTheSnapshotOfTheReservationItSpeaksFor()
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);

        await RunAsync(due, ledger, SessionRefusalTitles.NoLock);

        ReservationOutcome written = Assert.Single(ledger.Outcomes.Held);

        Assert.Equal(due.Snapshot.Name, written.SnapshotName);
        Assert.Equal(due.EffectiveStartAt, written.EffectiveStartAt);
        Assert.Equal(due.EffectiveEndAt, written.EffectiveEndAt);
        Assert.Equal(due.Priority, written.Priority);
        Assert.Equal(due.Programme.EventId, written.EventId);
    }

    [Theory]
    [InlineData(SessionRefusalTitles.NoDeviceFree)]
    [InlineData(SessionRefusalTitles.DeviceBusy)]
    public async Task AStartThatFoundNoFreeTunerIsWrittenDownAsAContestAndNotBlamedOnReception(string title)
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs.AddMinutes(-10), Airs.AddMinutes(20));
        recordings.Rows.Add(running);

        await RunAsync(due, ledger, title, recordings);

        ReservationOutcome written = Assert.Single(ledger.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.Competing, written.Kind);
        Assert.Null(written.TuneFailure);
        Assert.Equal([RecordingFault.TunerContended], written.Faults);
        Assert.Equal([running.ReservationId!.Value], written.RecordedInstead);
        Assert.Empty(ledger.Tuning.Failures);
    }

    [Fact]
    public async Task NothingIsTakenFromTheRecordingThatGotThereFirst()
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var recordings = new HeldRecordings();
        Recording running = InFlight(Airs.AddMinutes(-10), Airs.AddMinutes(20));
        recordings.Rows.Add(running);

        RecordingDriver driver = await RunAsync(due, ledger, SessionRefusalTitles.NoDeviceFree, recordings);

        Assert.Empty(driver.StopReasons);
        Assert.Empty(recordings.Saved);
        Assert.True(running.IsInFlight);
    }

    [Theory]
    [MemberData(nameof(WhatTheLedgerHasNoClassFor))]
    public async Task ARefusalNobodyCanClassifyIsLeftForTheWindowToSettle(string title)
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);

        await RunAsync(due, ledger, title);

        Assert.Empty(ledger.Outcomes.Held);
        Assert.Empty(ledger.Tuning.Failures);
    }

    [Fact]
    public async Task ADriverNobodyCouldReachSaysNothingAboutWhyAndWritesNothingDown()
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Unreachable("the socket was not there"),
            AnswersWhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("noSuchSession", [])),
        };

        await Round(due, ledger, driver, new HeldRecordings()).RunAsync(CancellationToken.None);

        Assert.Empty(ledger.Outcomes.Held);
    }

    [Fact]
    public async Task TheSameRefusalTickAfterTickLeavesOneRowInTheLedger()
    {
        RecordingTick due = Due(1);
        RefusalLedger ledger = new RefusalLedger().Knowing(due);
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(
                new DriverProblem(SessionRefusalTitles.NoLock, [])),
        };
        RecordingRound round = Round(due, ledger, driver, new HeldRecordings());

        await round.RunAsync(CancellationToken.None);
        await round.RunAsync(CancellationToken.None);
        await round.RunAsync(CancellationToken.None);

        Assert.Single(ledger.Outcomes.Held);
        Assert.Equal(3, ledger.Tuning.Failures.Count);
    }

    [Fact]
    public async Task AReservationThatIsGoneByTheTimeTheRefusalIsWrittenDownIsNotAnError()
    {
        RecordingTick due = Due(1);
        var ledger = new RefusalLedger();

        await RunAsync(due, ledger, SessionRefusalTitles.NoLock);

        Assert.Empty(ledger.Outcomes.Held);
    }

    [Fact]
    public async Task ARefusalIsHandedToTheLedgerAsAList()
        => await Assert.ThrowsAsync<ArgumentNullException>(
            () => new RefusalLedger().Reporter.ReportAsync(null!, Airs, CancellationToken.None));

    private static async Task<RecordingDriver> RunAsync(
        RecordingTick due,
        RefusalLedger ledger,
        string title,
        HeldRecordings? recordings = null)
    {
        var driver = new RecordingDriver
        {
            RefusesToStart = DriverCall<SessionSnapshot>.Refused(new DriverProblem(title, [])),
        };

        await Round(due, ledger, driver, recordings ?? new HeldRecordings()).RunAsync(CancellationToken.None);

        return driver;
    }

    private static RecordingRound Round(
        RecordingTick due,
        RefusalLedger ledger,
        RecordingDriver driver,
        HeldRecordings recordings)
    {
        var clock = new HeldMoment(Airs);

        return new RecordingRound(
            new PlannedReservations().Holding(due),
            recordings,
            new ResolvedTuning(Terrestrial),
            new DiskPrecheckService(new StorageMonitor(driver, clock, StorageMonitorSettings.Default)),
            driver,
            ledger.Reporter,
            Settings,
            clock);
    }
}
