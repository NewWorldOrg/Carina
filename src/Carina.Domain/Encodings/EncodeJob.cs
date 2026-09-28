using Carina.Domain.Base;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed class EncodeJob
{
    public const int FirstAttempt = 1;

    private EncodeJob()
    {
    }

    public EncodeJobId Id { get; private set; } = null!;

    public RecordingId RecordingId { get; private set; } = null!;

    public EncodeProfileId ProfileId { get; private set; } = null!;

    public EncodeDestinationId DestinationId { get; private set; } = null!;

    public OutputRoot OutputRoot { get; private set; } = null!;

    public EncodeJobStatus Status { get; private set; }

    public int Attempt { get; private set; }

    public DateTime QueuedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? EndedAt { get; private set; }

    public EncodeFailureDetail? Failure { get; private set; }

    public EncodeFileName? ArtefactName { get; private set; }

    /// <summary>
    /// Whether a person asked for the artefact of this recording and profile to be made again. It is
    /// settled when the job is queued.
    /// </summary>
    public bool MakesItAgain { get; private set; }

    /// <summary>
    /// When this job let go of the name it holds to a job asked to make the artefact again. What this
    /// job made is still named here.
    /// </summary>
    public DateTime? NameGivenUpAt { get; private set; }

    /// <summary>
    /// When a newer artefact of the same recording was made and this job's artefact stopped being the
    /// recording's one. Nothing while this job's artefact is the recording's one, or when it made none.
    /// </summary>
    public DateTime? ReplacedAt { get; private set; }

    public EncodeRoute? Route { get; private set; }

    public RunningProgramme? Programme { get; private set; }

    public EncodeHeadway? Headway { get; private set; }

    public EncodeTimeline? Timeline { get; private set; }

    public ChapterReading? Chapters { get; private set; }

    public bool HasEnded => EncodeStandings.IsTerminal(Status);

    /// <summary>
    /// Whether this job completed and what it made has not been replaced by a newer artefact.
    /// </summary>
    public bool StandsAsTheArtefact => Status is EncodeJobStatus.Completed && ArtefactName is not null && ReplacedAt is null;

    public EncodeStanding Standing => EncodeStandings.Of(Status);

    /// <summary>
    /// The artefact this job makes: the name it holds once it has named one, and before that the name
    /// it will be held to.
    /// </summary>
    public EncodeFileName ArtefactItMakes => ArtefactName ?? EncodeFileName.Artefact(RecordingId, ProfileId);

    public EncodeFileName WorkFileName => EncodeFileName.Working(RecordingId, Id, Attempt);

    public EncodeFileName ChaptersFileName => EncodeFileName.Chapters(RecordingId, Id, Attempt);

    public static EncodeJob Queue(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        DateTime at)
        => Waiting(id, recordingId, profileId, destinationId, outputRoot, at, makesItAgain: false);

    /// <summary>
    /// A job queued because a person asked for an artefact that already exists to be made again. It is
    /// the only kind that puts what it makes where an earlier artefact stands.
    /// </summary>
    public static EncodeJob QueueAgain(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        DateTime at)
        => Waiting(id, recordingId, profileId, destinationId, outputRoot, at, makesItAgain: true);

    private static EncodeJob Waiting(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        DateTime at,
        bool makesItAgain)
        => Rehydrate(
            id,
            recordingId,
            profileId,
            destinationId,
            outputRoot,
            EncodeJobStatus.Queued,
            FirstAttempt,
            at,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            makesItAgain);

    public static EncodeJob Rehydrate(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        EncodeJobStatus status,
        int attempt,
        DateTime queuedAt,
        DateTime? startedAt,
        DateTime? endedAt,
        EncodeFailureDetail? failure,
        EncodeFileName? artefactName,
        EncodeRoute? route,
        RunningProgramme? programme,
        EncodeHeadway? headway,
        EncodeTimeline? timeline,
        ChapterReading? chapters,
        bool makesItAgain = false,
        DateTime? nameGivenUpAt = null,
        DateTime? replacedAt = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(destinationId);
        ArgumentNullException.ThrowIfNull(outputRoot);

        if (attempt < FirstAttempt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempt),
                attempt,
                $"A job is on its {FirstAttempt}st attempt before it is on any other.");
        }

        if (artefactName is not null && !artefactName.Equals(EncodeFileName.Artefact(recordingId, profileId)))
        {
            throw new ArgumentException(
                "A job's artefact is named for its recording and its profile, and this name is for something else.",
                nameof(artefactName));
        }

        if (nameGivenUpAt is not null && artefactName is null)
        {
            throw new ArgumentException("A job gives up the name it holds, and this one names nothing.", nameof(nameGivenUpAt));
        }

        if (replacedAt is not null && (status is not EncodeJobStatus.Completed || artefactName is null))
        {
            throw new ArgumentException("Only a job that completed and named what it made is replaced.", nameof(replacedAt));
        }

        if (programme is not null && status is not EncodeJobStatus.Running)
        {
            throw new ArgumentException("Only a job the ledger holds as running has a programme of its own.", nameof(programme));
        }

        if ((route is not null || headway is not null || timeline is not null || chapters is not null)
            && status is EncodeJobStatus.Queued)
        {
            throw new ArgumentException("A job that is waiting has run nowhere and got nowhere.", nameof(route));
        }

        return new EncodeJob
        {
            Id = id,
            RecordingId = recordingId,
            ProfileId = profileId,
            DestinationId = destinationId,
            OutputRoot = outputRoot,
            Status = EncodeStandings.Named(status),
            Attempt = attempt,
            QueuedAt = UtcTimes.Required(queuedAt, nameof(queuedAt)),
            StartedAt = UtcTimes.Optional(startedAt, nameof(startedAt)),
            EndedAt = UtcTimes.Optional(endedAt, nameof(endedAt)),
            Failure = failure,
            ArtefactName = artefactName,
            MakesItAgain = makesItAgain,
            NameGivenUpAt = UtcTimes.Optional(nameGivenUpAt, nameof(nameGivenUpAt)),
            ReplacedAt = UtcTimes.Optional(replacedAt, nameof(replacedAt)),
            Route = route,
            Programme = programme,
            Headway = headway,
            Timeline = timeline,
            Chapters = chapters,
        };
    }

    public void Start(DateTime at)
    {
        Only(EncodeJobStatus.Queued, "start");

        Status = EncodeJobStatus.Running;
        StartedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Routed(EncodeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Only(EncodeJobStatus.Running, "say where it runs");

        Route = route;
    }

    public void Spawned(RunningProgramme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);
        Only(EncodeJobStatus.Running, "have a programme of its own");

        Programme = programme;
    }

    public void Reached(EncodeProgress progress, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Only(EncodeJobStatus.Running, "report headway");

        Headway = EncodeHeadway.Of(progress, at);
    }

    public void Aligned(EncodeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        Only(EncodeJobStatus.Running, "say where its clock stands");

        Timeline = timeline;
    }

    /// <summary>
    /// Writes down what the run made of where the breaks in this job's recording are, before the encode
    /// that bakes them in starts. It is written whatever the answer.
    /// </summary>
    public void Judged(ChapterReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Only(EncodeJobStatus.Running, "say where the breaks in it are");

        Chapters = reading;
    }

    public void Measured(TimeSpan artefactLength)
    {
        Only(EncodeJobStatus.Running, "measure its artefact");

        if (Timeline is null)
        {
            throw new InvalidOperationException("A job measures its artefact against the clock it was aligned to, and this one was never aligned.");
        }

        Timeline = Timeline.Measured(artefactLength);
    }

    /// <summary>
    /// How long a running job has gone without making headway, measured from its last report or,
    /// before the first, from when it started. Nothing for a job that is not running.
    /// </summary>
    public TimeSpan? QuietFor(DateTime now)
    {
        UtcTimes.Required(now, nameof(now));

        if (Status is not EncodeJobStatus.Running || StartedAt is not { } started)
        {
            return null;
        }

        DateTime lastHeard = Headway?.At ?? started;

        return now > lastHeard ? now - lastHeard : TimeSpan.Zero;
    }

    /// <summary>
    /// Whether a running job has made no headway for as long as a run is allowed to go quiet.
    /// </summary>
    public bool IsStalled(DateTime now, TimeSpan stalledAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stalledAfter, TimeSpan.Zero);

        return QuietFor(now) is { } quiet && quiet >= stalledAfter;
    }

    public void Name(EncodeFileName artefactName)
    {
        ArgumentNullException.ThrowIfNull(artefactName);
        Only(EncodeJobStatus.Running, "name its artefact");

        if (!artefactName.Equals(EncodeFileName.Artefact(RecordingId, ProfileId)))
        {
            throw new InvalidOperationException(
                "A job's artefact is named for its recording and its profile, and this name is for something else.");
        }

        ArtefactName = artefactName;
    }

    /// <summary>
    /// Lets go of the name in the ledger so that a job asked to make the artefact again can hold it.
    /// What this job made is still named here; the file at that name is replaced by the job that now
    /// holds it.
    /// </summary>
    public void GiveUpTheName(DateTime at)
    {
        if (ArtefactName is null)
        {
            throw new InvalidOperationException("A job gives up the name it holds, and this one names nothing.");
        }

        NameGivenUpAt = UtcTimes.Required(at, nameof(at));
    }

    /// <summary>
    /// Whether this job's artefact and <paramref name="other"/>'s are the same file: the same name under
    /// the same output root.
    /// </summary>
    public bool SharesTheArtefactWith(EncodeJob other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return ArtefactName is { } name && name.Equals(other.ArtefactName) && OutputRoot.Equals(other.OutputRoot);
    }

    /// <summary>
    /// Marks what this job made as replaced by <paramref name="newer"/>, a later completed job of the
    /// same recording.
    /// </summary>
    public void Replaced(EncodeJob newer, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(newer);

        if (!StandsAsTheArtefact)
        {
            throw new InvalidOperationException(
                "Only a job that completed, named what it made and has not been replaced yet is replaced.");
        }

        if (newer.Id.Equals(Id) || !newer.RecordingId.Equals(RecordingId) || !newer.StandsAsTheArtefact)
        {
            throw new ArgumentException(
                "An artefact is replaced by another completed job of the same recording whose artefact stands.",
                nameof(newer));
        }

        DateTime when = UtcTimes.Required(at, nameof(at));

        if (when < EndedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "An artefact is replaced after it was made, not before.");
        }

        ReplacedAt = when;
    }

    public void Complete(DateTime at)
    {
        Only(EncodeJobStatus.Running, "complete");

        if (ArtefactName is null)
        {
            throw new InvalidOperationException("A job completes by saying what it made, and this one has named nothing.");
        }

        Status = EncodeJobStatus.Completed;
        EndedAt = UtcTimes.Required(at, nameof(at));
        Programme = null;
    }

    public void Fail(EncodeFailure failure, string note, DateTime at)
    {
        Only(EncodeJobStatus.Running, "fail");

        Status = EncodeJobStatus.Failed;
        EndedAt = UtcTimes.Required(at, nameof(at));
        Failure = new EncodeFailureDetail(failure, note, at);
        Programme = null;
    }

    public void Cancel(DateTime at)
    {
        if (Status is not (EncodeJobStatus.Queued or EncodeJobStatus.Running))
        {
            throw new InvalidOperationException($"A job that stands at {Status} cannot be called off.");
        }

        Status = EncodeJobStatus.Cancelled;
        EndedAt = UtcTimes.Required(at, nameof(at));
        Programme = null;
    }

    public void Requeue(DateTime at)
    {
        Only(EncodeJobStatus.Running, "put back in the queue");

        Status = EncodeJobStatus.Queued;
        Attempt++;
        QueuedAt = UtcTimes.Required(at, nameof(at));
        StartedAt = null;
        Route = null;
        Programme = null;
        Headway = null;
        Timeline = null;
        Chapters = null;
    }

    /// <summary>
    /// Puts a job the ledger holds as running back in the queue to start over, or gives it up when it
    /// has already had as many attempts as it gets. Called when the process comes up and when a run
    /// throws.
    /// </summary>
    public EncodeRecovery Recover(int mostAttempts, DateTime at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mostAttempts, FirstAttempt);
        Only(EncodeJobStatus.Running, "be picked up again");

        if (Attempt >= mostAttempts)
        {
            Fail(
                EncodeFailure.TimedOut,
                $"the job was found running when the process came up, on attempt {Attempt} of the {mostAttempts} it gets, so it is not tried again",
                at);

            return EncodeRecovery.GivenUp;
        }

        Requeue(at);

        return EncodeRecovery.PutBack;
    }

    private void Only(EncodeJobStatus expected, string move)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"A job that stands at {Status} cannot {move}; only one at {expected} can.");
        }
    }
}
