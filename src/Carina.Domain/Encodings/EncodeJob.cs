using Carina.Domain.Base;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed class EncodeJob
{
    public const int FirstAttempt = 1;

    public const int CaptionTrackTriesAtMost = 3;

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

    public bool MakesItAgain { get; private set; }

    public DateTime? NameGivenUpAt { get; private set; }

    public DateTime? ReplacedAt { get; private set; }

    public EncodeRoute? Route { get; private set; }

    public RunningProgramme? Programme { get; private set; }

    public EncodeHeadway? Headway { get; private set; }

    public EncodeTimeline? Timeline { get; private set; }

    public ChapterReading? Chapters { get; private set; }

    public EncodeCaptionTrack? CaptionTrack { get; private set; }

    public DateTime? CaptionTrackFrom { get; private set; }

    public int CaptionTrackAttempts { get; private set; }

    public EncodeRemake? Remade { get; private set; }

    public bool HasEnded => EncodeStandings.IsTerminal(Status);

    public bool StandsAsTheArtefact => Status is EncodeJobStatus.Completed && ArtefactName is not null && ReplacedAt is null;

    public EncodeStanding Standing => EncodeStandings.Of(Status);

    public bool ClaimsItsArtefact(bool recordingHeld) => !HasEnded || (StandsAsTheArtefact && recordingHeld);

    public EncodeFileName ArtefactItMakes => ArtefactName ?? EncodeFileName.Artefact(RecordingId, ProfileId);

    public EncodeFileName WorkFileName => EncodeFileName.Working(RecordingId, Id, Attempt);

    public EncodeFileName RemadeFileName => EncodeFileName.Remade(RecordingId, Id, Attempt);

    public EncodeFileName KeptWorkFileName
        => Remade is null or EncodeRemake.NoSmallerTheSecondTime ? WorkFileName : RemadeFileName;

    public EncodeFileName ChaptersFileName => EncodeFileName.Chapters(RecordingId, Id, Attempt);

    public EncodeFileName CaptionTrackFileName => EncodeFileName.CaptionTrack(RecordingId, Id, Attempt);

    public EncodeFileName CaptionedFileName => EncodeFileName.Captioned(RecordingId, Id, Attempt);

    public EncodeFileName FileToPlace => CaptionTrack is EncodeCaptionTrack.Added ? CaptionedFileName : KeptWorkFileName;

    public static EncodeJob Queue(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        DateTime at)
        => Waiting(id, recordingId, profileId, destinationId, outputRoot, at, makesItAgain: false);

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
        DateTime? replacedAt = null,
        EncodeCaptionTrack? captionTrack = null,
        DateTime? captionTrackFrom = null,
        int captionTrackAttempts = 0,
        EncodeRemake? remade = null)
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

        if ((captionTrack is null) != (captionTrackFrom is null))
        {
            throw new ArgumentException("A text track of captions is settled with what became of it and when the captions it came from were taken, or not at all.", nameof(captionTrack));
        }

        if (captionTrackAttempts < 0 || (captionTrackAttempts > 0 && captionTrack is not EncodeCaptionTrack.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(captionTrackAttempts), captionTrackAttempts, "Failures of a text track are counted only while the track has failed.");
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

        if (remade is not null && status is EncodeJobStatus.Queued)
        {
            throw new ArgumentException("A job that is waiting has made nothing, let alone made it again.", nameof(remade));
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
            CaptionTrack = captionTrack is { } track ? Track(track) : null,
            CaptionTrackFrom = UtcTimes.Optional(captionTrackFrom, nameof(captionTrackFrom)),
            CaptionTrackAttempts = captionTrackAttempts,
            Remade = remade is { } again ? Again(again) : null,
        };
    }

    public bool AwaitsCaptionTrack(DateTime captionsMadeAt)
    {
        DateTime madeAt = UtcTimes.Required(captionsMadeAt, nameof(captionsMadeAt));

        if (!StandsAsTheArtefact)
        {
            return false;
        }

        return CaptionTrackFrom != madeAt
            || (CaptionTrack is EncodeCaptionTrack.Failed && CaptionTrackAttempts < CaptionTrackTriesAtMost);
    }

    public void Tracked(EncodeCaptionTrack outcome, DateTime from)
    {
        EncodeCaptionTrack settled = Track(outcome);
        DateTime madeAt = UtcTimes.Required(from, nameof(from));

        if (Status is not EncodeJobStatus.Running && !StandsAsTheArtefact)
        {
            throw new InvalidOperationException(
                $"A text track of captions is put into an artefact being made or one that stands, and this job stands at {Status}.");
        }

        int before = CaptionTrack is EncodeCaptionTrack.Failed && CaptionTrackFrom == madeAt ? CaptionTrackAttempts : 0;

        CaptionTrack = settled;
        CaptionTrackFrom = madeAt;
        CaptionTrackAttempts = settled is EncodeCaptionTrack.Failed ? before + 1 : 0;
    }

    public void Remaking()
    {
        Only(EncodeJobStatus.Running, "make its artefact again");

        if (Remade is not null)
        {
            throw new InvalidOperationException("An attempt makes its artefact again once, and this one has already.");
        }

        Remade = EncodeRemake.LargerThanTheSource;
    }

    public void MadeAgain(EncodeRemake kept)
    {
        EncodeRemake judged = Again(kept);
        Only(EncodeJobStatus.Running, "say what it kept after making it again");

        if (Remade is not EncodeRemake.LargerThanTheSource)
        {
            throw new InvalidOperationException("Only an attempt making its artefact again says what it kept, and says it once.");
        }

        Remade = judged;
    }

    private static EncodeRemake Again(EncodeRemake remade)
        => Enum.IsDefined(remade)
            ? remade
            : throw new ArgumentOutOfRangeException(nameof(remade), remade, "An artefact is made again for one of the reasons named here.");

    private static EncodeCaptionTrack Track(EncodeCaptionTrack track)
        => Enum.IsDefined(track)
            ? track
            : throw new ArgumentOutOfRangeException(nameof(track), track, "A text track of captions ends in one of the ways named here.");

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

        Programme = programme.NoEarlierThan(StartedAt!.Value);
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

    public void Judged(ChapterReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Only(EncodeJobStatus.Running, "say where the breaks in it are");

        Chapters = reading;
    }

    public void Rechaptered(ChapterReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (!StandsAsTheArtefact)
        {
            throw new InvalidOperationException(
                $"The chapters of an artefact that stands are compared again, and this job stands at {Status}.");
        }

        if (reading.Detector is not ChapterDetectorName.Segments)
        {
            throw new ArgumentException("The chapters of an artefact that stands are compared with the segments only.", nameof(reading));
        }

        if (reading.DecidedAt < EndedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(reading), reading.DecidedAt, "The chapters are compared again after the job ended.");
        }

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

    public void GiveUpTheName(DateTime at)
    {
        if (ArtefactName is null)
        {
            throw new InvalidOperationException("A job gives up the name it holds, and this one names nothing.");
        }

        NameGivenUpAt = UtcTimes.Required(at, nameof(at));
    }

    public bool SharesTheArtefactWith(EncodeJob other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return ArtefactName is { } name && name.Equals(other.ArtefactName) && OutputRoot.Equals(other.OutputRoot);
    }

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
        LetGoOfTheName(at);
    }

    public EncodeCallOff CallOff(DateTime at)
    {
        if (HasEnded)
        {
            return EncodeCallOff.AlreadyOver;
        }

        Cancel(at);

        return EncodeCallOff.CalledOff;
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
        LetGoOfTheName(at);
    }

    private void LetGoOfTheName(DateTime at)
    {
        if (ArtefactName is not null && NameGivenUpAt is null)
        {
            NameGivenUpAt = at;
        }
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
        CaptionTrack = null;
        CaptionTrackFrom = null;
        CaptionTrackAttempts = 0;
        Remade = null;
    }

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
