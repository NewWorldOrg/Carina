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
    /// Whether a person asked for the artefact of this recording and profile to be made again. It
    /// is settled when the job is queued and never worked out afterwards, because it is the one
    /// thing that lets a run put its artefact where an earlier one already stands.
    /// </summary>
    public bool MakesItAgain { get; private set; }

    /// <summary>
    /// When this job let go of the name it holds, so that a job asked to make the artefact again
    /// could take it. What this job made is still named here; only the claim moved.
    /// </summary>
    public DateTime? NameGivenUpAt { get; private set; }

    public EncodeRoute? Route { get; private set; }

    public RunningProgramme? Programme { get; private set; }

    public EncodeHeadway? Headway { get; private set; }

    public EncodeTimeline? Timeline { get; private set; }

    public ChapterReading? Chapters { get; private set; }

    public bool HasEnded => EncodeStandings.IsTerminal(Status);

    public EncodeStanding Standing => EncodeStandings.Of(Status);

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
    /// A job queued because a person asked for an artefact that already exists to be made again.
    /// It is the only kind that puts what it makes where an earlier artefact stands, and it says so
    /// from the moment it is queued rather than having the intent worked out at the end of the run.
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
        DateTime? nameGivenUpAt = null)
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
    /// What the run made of where the breaks in this job's recording are, written down before the
    /// encode that bakes them in starts. It is written whatever the answer, so that a job nobody
    /// looked at says so rather than looking like one from before anything looked.
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
    /// A running job that has made no headway for as long as a run is allowed to go quiet. The
    /// ledger says running; this is what says it should not be read that way (BR-ED2-014).
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
    /// Lets go of the name in the ledger, so that a job a person asked to make the artefact again
    /// can hold it instead. What this job made is left alone and still named here: only the claim
    /// moves, and the file at that name is replaced by the job that now holds it.
    /// </summary>
    public void GiveUpTheName(DateTime at)
    {
        if (ArtefactName is null)
        {
            throw new InvalidOperationException("A job gives up the name it holds, and this one names nothing.");
        }

        NameGivenUpAt = UtcTimes.Required(at, nameof(at));
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
    /// What happens to a job the ledger still holds as running when the process comes up: the run
    /// it was on died with the process, so it goes back to the queue to start over, unless it has
    /// already had as many attempts as it gets (BR-ED2-011).
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
