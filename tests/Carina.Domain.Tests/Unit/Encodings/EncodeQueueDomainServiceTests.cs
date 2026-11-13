using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Tests.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeQueueDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc);

    private readonly HeldEncodeJobs jobs = new();

    private readonly HeldEncodeProfiles profiles = new();

    private readonly HeldEncodeDestinations destinations = new();

    private readonly HeldRecordings recordings = new();

    private readonly EncodeProfile profile;

    private readonly EncodeDestination destination;

    private readonly Recording recording;

    public EncodeQueueDomainServiceTests()
    {
        profile = Profile();
        destination = Destination(profile.Id);
        recording = Ended(RecordingOutcome.Complete);
        profiles.Profiles.Add(profile);
        destinations.Destinations.Add(destination);
        recordings.Recordings.Add(recording);
    }

    private EncodeQueueDomainService Service => new(jobs, profiles, destinations, recordings);

    [Fact(DisplayName = "a recording that has ended is queued with the destination's own profile when none is named, to the destination's output root")]
    public async Task ARecordingIsQueuedWithTheDestinationsOwnProfile()
    {
        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Null(queueing.Refusal);
        EncodeJob queued = Assert.IsType<EncodeJob>(queueing.Job);
        Assert.Equal(recording.Id, queued.RecordingId);
        Assert.Equal(profile.Id, queued.ProfileId);
        Assert.Equal(destination.Id, queued.DestinationId);
        Assert.Equal(destination.OutputRoot, queued.OutputRoot);
        Assert.Equal(EncodeJobStatus.Queued, queued.Status);
        Assert.Equal(At, queued.QueuedAt);
        Assert.False(queued.MakesItAgain);
        Assert.Empty(jobs.Jobs);
    }

    [Fact(DisplayName = "BR-ED2-001: a recording asked to be made again is queued as a job that makes it again, beside the artefact it has")]
    public async Task ARecordingAskedToBeMadeAgainIsQueuedAsSuch()
    {
        jobs.Jobs.Add(Completed(recording.Id));

        EncodeQueueing queueing = await Service.QueueAsync(Asked(makeItAgain: true), At, Cancel);

        Assert.True(Assert.IsType<EncodeJob>(queueing.Job).MakesItAgain);
    }

    [Fact(DisplayName = "a destination that is not defined is refused, before anything else is read")]
    public async Task AnUndefinedDestinationIsRefused()
    {
        EncodeQueueing queueing = await Service.QueueAsync(
            new EncodeJobAsked(RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), false),
            At,
            Cancel);

        Assert.Equal(EncodeQueueRefusal.NoSuchDestination, queueing.Refusal);
        Assert.Null(queueing.Job);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired destination takes no new job, and is handed back to be named")]
    public async Task ARetiredDestinationTakesNoNewJob()
    {
        destination.Retire(At.AddDays(-1));

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.DestinationRetired, queueing.Refusal);
        Assert.Same(destination, queueing.Destination);
    }

    [Fact(DisplayName = "a profile that is not defined is refused, naming the profile the destination would have used")]
    public async Task AnUndefinedProfileIsRefused()
    {
        profiles.Profiles.Clear();

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.NoSuchProfile, queueing.Refusal);
        Assert.Equal(profile.Id, queueing.ProfileId);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired profile encodes nothing again, and is handed back to be named")]
    public async Task ARetiredProfileEncodesNothingAgain()
    {
        profile.Retire(At.AddDays(-1));

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.ProfileRetired, queueing.Refusal);
        Assert.Same(profile, queueing.Profile);
    }

    [Fact(DisplayName = "a recording the ledger does not hold is refused")]
    public async Task AnUnheldRecordingIsRefused()
    {
        recordings.Recordings.Clear();

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.NoSuchRecording, queueing.Refusal);
    }

    [Fact(DisplayName = "a recording still being written is refused")]
    public async Task ARecordingStillBeingWrittenIsRefused()
    {
        recordings.Recordings.Clear();
        recordings.Recordings.Add(Begun(recording.Id));

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.RecordingStillBeingWritten, queueing.Refusal);
    }

    [Fact(DisplayName = "BR-ED2-004: a recording that failed has nothing to encode")]
    public async Task ARecordingThatFailedIsRefused()
    {
        recordings.Recordings.Clear();
        recordings.Recordings.Add(Ended(RecordingOutcome.Failed, recording.Id));

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.RecordingFailed, queueing.Refusal);
    }

    [Fact(DisplayName = "a recording with a job waiting or running is not queued twice, and that job is handed back, whether or not it is asked to be made again")]
    public async Task ARecordingWithAJobUnderWayIsNotQueuedTwice()
    {
        EncodeJob waiting = Queued(recording.Id);
        jobs.Jobs.Add(waiting);

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);
        EncodeQueueing again = await Service.QueueAsync(Asked(makeItAgain: true), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.AlreadyInTheQueue, queueing.Refusal);
        Assert.Same(waiting, queueing.Earlier);
        Assert.Equal(EncodeQueueRefusal.AlreadyInTheQueue, again.Refusal);
    }

    [Fact(DisplayName = "BR-ED2-001: a recording with a completed job, whatever its profile, is refused unless asked to be made again, naming the latest one")]
    public async Task ARecordingWithAnArtefactIsRefused()
    {
        EncodeJob older = Completed(recording.Id);
        EncodeJob latest = Completed(recording.Id);
        jobs.Jobs.AddRange([older, latest]);

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.Equal(EncodeQueueRefusal.AlreadyEncoded, queueing.Refusal);
        Assert.Same(latest, queueing.Earlier);
    }

    [Fact(DisplayName = "a recording whose earlier jobs failed or were called off is queued again")]
    public async Task ARecordingWhoseJobsDidNotCompleteIsQueued()
    {
        EncodeJob calledOff = Queued(recording.Id);
        calledOff.Cancel(At.AddHours(-1));
        jobs.Jobs.Add(calledOff);

        EncodeQueueing queueing = await Service.QueueAsync(Asked(), At, Cancel);

        Assert.NotNull(queueing.Job);
    }

    private EncodeJobAsked Asked(bool makeItAgain = false)
        => new(recording.Id, null, destination.Id, makeItAgain);

    private EncodeJob Queued(RecordingId id)
        => EncodeJob.Queue(EncodeJobId.New(), id, profile.Id, destination.Id, destination.OutputRoot, At.AddHours(-2));

    private EncodeJob Completed(RecordingId id)
    {
        EncodeJob job = EncodeJob.Queue(EncodeJobId.New(), id, EncodeProfileId.New(), destination.Id, destination.OutputRoot, At.AddHours(-2));
        job.Start(At.AddHours(-2));
        job.Name(EncodeFileName.Artefact(id, job.ProfileId));
        job.Complete(At.AddHours(-1));

        return job;
    }

    private static EncodeProfile Profile()
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At.AddDays(-10));

    private static EncodeDestination Destination(EncodeProfileId profile)
        => EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            new OutputRoot("shelf"),
            profile,
            At.AddDays(-10));

    private static Recording Ended(RecordingOutcome outcome, RecordingId? id = null)
    {
        Recording started = RecordingFactory.Started(id);

        if (outcome is RecordingOutcome.Complete)
        {
            started.Abort(RecordingFactory.Now.AddHours(1));
        }
        else
        {
            started.Note(RecordingFactory.Fault());
        }

        started.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000_000, RecordingFactory.Now.AddHours(1));

        return started;
    }

    private static Recording Begun(RecordingId id) => RecordingFactory.Started(id);
}
