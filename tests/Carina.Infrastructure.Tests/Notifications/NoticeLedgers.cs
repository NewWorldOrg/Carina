using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Reservations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Carina.Infrastructure.Tests.Notifications;

/// <summary>
/// Writes rows of the ledgers a notice is taken from into the scratch database, and looks at them as the loop does.
/// </summary>
internal sealed class NoticeLedgers(RepositoryDatabase database)
{
    public const string ProgrammeName = "A made-up programme";

    public const string StationName = "Made-up Television";

    private const int Network = 32736;

    private const int NamedService = 1984;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    public async Task EmptyTheNoticesAsync()
    {
        await using CarinaDbContext context = database.Open();
        await using NpgsqlConnection connection = new(context.Database.GetConnectionString());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand emptying = new(
            "DELETE FROM notification_event; DELETE FROM notification_rule; DELETE FROM notification_destination; "
            + "DELETE FROM notification_watch;",
            connection);
        await emptying.ExecuteNonQueryAsync(Cancel);
    }

    public async Task EmptyTheEncodesAsync()
    {
        await using CarinaDbContext context = database.Open();
        await context.Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await context.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await context.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    public async Task<NotificationIntake> LookAsync(DateTime now, NoticeLinkBase? links = null, long? artefactBytes = null)
    {
        await using CarinaDbContext context = database.Open();
        NoticeRepository notices = new(context);
        NotificationIntakeRound round = new(
            new NotificationIntakeDomainService(
                new NoticeSourceReader(context, new ArtefactsThatWeigh(artefactBytes)),
                notices,
                new StoppedClock(now)),
            notices,
            new NotificationRuleRepository(context),
            new NotificationDestinationRepository(context),
            new NotificationIntakeSettings(),
            links ?? NoticeLinkBase.None,
            NullLogger<NotificationIntakeRound>.Instance);

        return await round.TakeAsync(Cancel);
    }

    public async Task<IReadOnlyList<Notice>> NoticesOfAsync(Guid sourceId)
    {
        await using CarinaDbContext context = database.Open();

        return await context.Set<Notice>()
            .AsNoTracking()
            .Where(notice => notice.SourceId == sourceId)
            .OrderBy(notice => notice.OccurredAt)
            .ToListAsync(Cancel);
    }

    public async Task<IReadOnlyList<NoticeKind>> KindsOfAsync(Guid sourceId)
        => [.. (await NoticesOfAsync(sourceId)).Select(notice => notice.Kind)];

    public async Task<IReadOnlyList<NotificationDelivery>> DeliveriesOfAsync(Guid sourceId, NoticeKind kind)
    {
        Notice notice = (await NoticesOfAsync(sourceId)).Single(held => held.Kind == kind);
        await using CarinaDbContext context = database.Open();

        return await new NotificationDeliveryRepository(context).ListOfAsync(notice.Id, Cancel);
    }

    public async Task StationIsNamedAsync(DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        BroadcastServiceRepository stations = new(context);

        if (await stations.FindAsync(new NetworkId(Network), new ServiceId(NamedService), Cancel) is not null)
        {
            return;
        }

        await stations.AddAsync(
            BroadcastService.Discover(
                new NetworkId(Network),
                new ServiceId(NamedService),
                StationName,
                ServiceCategory.Television,
                at),
            Cancel);
    }

    public async Task<RecordingId> RecordingBeganAsync(DateTime began, ReservationId? reservation = null)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            reservation,
            ReservationFixtures.Programme(ReservationFixtures.NextEventId(), NamedService, began),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
            began,
            began.AddMinutes(30),
            new ProgrammeSnapshot(
                ProgrammeName,
                string.Empty,
                string.Empty,
                [],
                began,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            began,
            new TunerDeviceId("synthetic-0"));

        await using CarinaDbContext context = database.Open();
        context.Add(recording);
        await context.SaveChangesAsync(Cancel);

        return id;
    }

    public async Task RecordingEndedAsync(RecordingId id, RecordingOutcome outcome, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        Recording recording = await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording to end was never written.");

        recording.Abort(at);

        if (outcome is not RecordingOutcome.Complete)
        {
            recording.Note(new OutcomeDetail(RecordingFault.ShortOfTheWindow, null, "the supply stopped before the window did", at));
        }

        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_200_000, at);
        await context.SaveChangesAsync(Cancel);
    }

    public async Task RecordingEndedScrambledAsync(RecordingId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        Recording recording = await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording to end was never written.");

        recording.Abort(at);
        recording.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, "the picture was still scrambled when the window closed", at));
        recording.Settle(RecordingOutcome.Complete, 1_200_000, at);
        await context.SaveChangesAsync(Cancel);
    }

    public async Task<ReservationId> ReservationIsPlannedAsync(string name, DateTime startsAt)
    {
        Reservation reservation = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), NamedService, startsAt),
            snapshot: ReservationFixtures.Snapshot(name));

        await using CarinaDbContext context = database.Open();
        await new ReservationRepository(context).AddAsync(reservation, Cancel);

        return reservation.Id;
    }

    public async Task<ReservationOutcomeId> OutcomeAsync(
        ReservationOutcomeKind kind,
        DateTime at,
        ReservationId? reservation = null,
        TuneFailureKind? tuneFailure = null,
        RecordingOutcome? recordingOutcome = null,
        IReadOnlyList<RecordingFault>? faults = null,
        IReadOnlyList<Guid>? recordedInstead = null,
        RetryResult? retryResult = null,
        RetryGiveUp? gaveUpBecause = null)
    {
        ReservationOutcomeId id = ReservationOutcomeId.New();
        ReservationOutcome outcome = ReservationOutcome.Rehydrate(
            id,
            reservation ?? ReservationId.New(),
            ReservationFixtures.Programme(ReservationFixtures.NextEventId(), NamedService, at),
            ProgrammeName,
            at,
            at.AddMinutes(30),
            Priority.Default,
            null,
            kind,
            tuneFailure,
            recordingOutcome,
            faults ?? [],
            recordedInstead ?? [],
            at,
            retryResult,
            gaveUpBecause);

        await using CarinaDbContext context = database.Open();
        await new ReservationOutcomeRepository(context).AddAsync(outcome, Cancel);

        return id;
    }

    public async Task<EncodeJobId> EncodeIsQueuedAsync(RecordingId recording, DateTime at)
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            at);
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            new OutputRoot("encodes"),
            profile.Id,
            at);
        EncodeJob job = EncodeJob.Queue(EncodeJobId.New(), recording, profile.Id, destination.Id, destination.OutputRoot, at);

        await using CarinaDbContext context = database.Open();
        await new EncodeProfileRepository(context).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(context).AddAsync(destination, Cancel);
        await new EncodeJobRepository(context).AddAsync(job, Cancel);

        return job.Id;
    }

    public Task EncodeStartedAsync(EncodeJobId id, DateTime at) => EncodeAsync(id, job => job.Start(at));

    public Task EncodeCompletedAsync(EncodeJobId id, DateTime at)
        => EncodeAsync(
            id,
            job =>
            {
                job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId));
                job.Complete(at);
            });

    public Task EncodeFailedAsync(EncodeJobId id, DateTime at)
        => EncodeAsync(id, job => job.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", at));

    public async Task<QualityIncidentId> ReceptionFellAsync(DateTime detectedAt)
    {
        QualityIncident incident = QualityIncident.Detect(
            QualityIncidentId.New(),
            detectedAt,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N")),
            0.004,
            Threshold.Provisionally(0.0002, observations: 0, detectedAt));

        await using CarinaDbContext context = database.Open();
        await new QualityIncidentRepository(context).AddAsync(incident, Cancel);

        return incident.Id;
    }

    public async Task<QualityIncidentId> TunerFailedToLockAsync(DateTime detectedAt)
    {
        QualityIncident incident = QualityIncident.Detect(
            QualityIncidentId.New(),
            detectedAt,
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "synthetic-0"),
            0,
            Threshold.Provisionally(0.9, observations: 0, detectedAt),
            QualityIncidentOwner.Tuner,
            nameof(TuneFailureKind.NoLock));

        await using CarinaDbContext context = database.Open();
        await new QualityIncidentRepository(context).AddAsync(incident, Cancel);

        return incident.Id;
    }

    public Task ShownOnTheScreenAsync(QualityIncidentId id, DateTime at) => IncidentAsync(id, incident => incident.Notify(at));

    public Task ReceptionCameBackAsync(QualityIncidentId id, DateTime at) => IncidentAsync(id, incident => incident.Resolve(at));

    public async Task<NotificationDestinationId> DestinationAsync(string name, DateTime at, bool enabled = true)
    {
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName(name),
            WebhookTarget.Slack,
            [0x01, 0x02, 0x03],
            "0000",
            at);

        if (!enabled)
        {
            destination.Disable(at);
        }

        await using CarinaDbContext context = database.Open();
        await new NotificationDestinationRepository(context).AddAsync(destination, Cancel);

        return destination.Id;
    }

    public async Task SwitchedOffAsync(NotificationDestinationId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        NotificationDestinationRepository destinations = new(context);
        NotificationDestination destination = await destinations.FindAsync(id, Cancel)
            ?? throw new InvalidOperationException("The destination to switch off was never registered.");

        destination.Disable(at);
        await destinations.SaveAsync(destination, Cancel);
    }

    public async Task<NotificationRuleId> RuleAsync(
        string name,
        DateTime at,
        IReadOnlyList<NoticeKind> events,
        IReadOnlyList<NotificationDestinationId> destinations,
        bool enabled = true)
    {
        NotificationRule rule = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName(name),
            events,
            destinations,
            at);

        if (!enabled)
        {
            rule.Disable(at);
        }

        await using CarinaDbContext context = database.Open();
        await new NotificationRuleRepository(context).AddAsync(rule, Cancel);

        return rule.Id;
    }

    private async Task EncodeAsync(EncodeJobId id, Action<EncodeJob> turn)
    {
        await using CarinaDbContext context = database.Open();
        EncodeJobRepository jobs = new(context);
        EncodeJob job = await jobs.FindAsync(id, Cancel)
            ?? throw new InvalidOperationException("The encode to turn was never queued.");

        turn(job);
        await jobs.SaveAsync(job, Cancel);
    }

    private async Task IncidentAsync(QualityIncidentId id, Action<QualityIncident> turn)
    {
        await using CarinaDbContext context = database.Open();
        QualityIncident incident = await context.FindAsync<QualityIncident>([id], Cancel)
            ?? throw new InvalidOperationException("The incident to turn was never detected.");

        turn(incident);
        await new QualityIncidentRepository(context).SaveAsync(incident, Cancel);
    }

    private sealed class ArtefactsThatWeigh(long? bytes) : IEncodeArtefactWeigher
    {
        public long? Weigh(OutputRoot root, EncodeFileName name) => bytes;
    }

    private sealed class StoppedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at);
    }
}
