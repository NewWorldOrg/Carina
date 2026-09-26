using Carina.Contracts;
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

public sealed class RecordingRetryTests
{
    private static readonly RetryPolicy Policy = new(2, TimeSpan.FromMinutes(1));

    private static readonly DriverCall<SessionSnapshot> WouldNotLock =
        DriverCall<SessionSnapshot>.Refused(new DriverProblem(SessionRefusalTitles.NoLock, []));

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
