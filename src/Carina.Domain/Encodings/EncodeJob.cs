using Carina.Domain.Base;
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

    public bool HasEnded => EncodeStandings.IsTerminal(Status);

    public EncodeStanding Standing => EncodeStandings.Of(Status);

    public EncodeFileName WorkFileName => EncodeFileName.Working(RecordingId, Id, Attempt);

    public static EncodeJob Queue(
        EncodeJobId id,
        RecordingId recordingId,
        EncodeProfileId profileId,
        EncodeDestinationId destinationId,
        OutputRoot outputRoot,
        DateTime at)
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
            null);

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
        EncodeFileName? artefactName)
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
        };
    }

    public void Start(DateTime at)
    {
        Only(EncodeJobStatus.Queued, "start");

        Status = EncodeJobStatus.Running;
        StartedAt = UtcTimes.Required(at, nameof(at));
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

    public void Complete(DateTime at)
    {
        Only(EncodeJobStatus.Running, "complete");

        if (ArtefactName is null)
        {
            throw new InvalidOperationException("A job completes by saying what it made, and this one has named nothing.");
        }

        Status = EncodeJobStatus.Completed;
        EndedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Fail(EncodeFailure failure, string note, DateTime at)
    {
        Only(EncodeJobStatus.Running, "fail");

        Status = EncodeJobStatus.Failed;
        EndedAt = UtcTimes.Required(at, nameof(at));
        Failure = new EncodeFailureDetail(failure, note, at);
    }

    public void Cancel(DateTime at)
    {
        if (Status is not (EncodeJobStatus.Queued or EncodeJobStatus.Running))
        {
            throw new InvalidOperationException($"A job that stands at {Status} cannot be called off.");
        }

        Status = EncodeJobStatus.Cancelled;
        EndedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Requeue(DateTime at)
    {
        Only(EncodeJobStatus.Running, "put back in the queue");

        Status = EncodeJobStatus.Queued;
        Attempt++;
        QueuedAt = UtcTimes.Required(at, nameof(at));
        StartedAt = null;
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
