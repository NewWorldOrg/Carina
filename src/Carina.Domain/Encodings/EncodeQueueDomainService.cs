using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// One recording asked to be encoded by hand: where the artefact goes, with which profile when not the destination's
/// own, and whether an artefact it already has is to be made again.
/// </summary>
public sealed record EncodeJobAsked(
    RecordingId RecordingId,
    EncodeProfileId? ProfileId,
    EncodeDestinationId DestinationId,
    bool MakeItAgain);

/// <summary>
/// Why a recording asked to be encoded by hand was not queued.
/// </summary>
public enum EncodeQueueRefusal
{
    NoSuchDestination = 1,

    DestinationRetired = 2,

    NoSuchProfile = 3,

    ProfileRetired = 4,

    NoSuchRecording = 5,

    RecordingStillBeingWritten = 6,

    RecordingFailed = 7,

    AlreadyInTheQueue = 8,

    AlreadyEncoded = 9,
}

/// <summary>
/// The answer to queuing a recording by hand: the job to be added, or why not, with what was read on the way
/// there to name in the answer. <see cref="ProfileId"/> is the profile asked for, or the destination's own;
/// <see cref="Earlier"/> is the job already waiting or running, or the latest completed one.
/// </summary>
public sealed record EncodeQueueing
{
    private EncodeQueueing(
        EncodeJob? job,
        EncodeQueueRefusal? refusal,
        EncodeDestination? destination,
        EncodeProfileId? profileId,
        EncodeProfile? profile,
        Recording? recording,
        EncodeJob? earlier)
    {
        Job = job;
        Refusal = refusal;
        Destination = destination;
        ProfileId = profileId;
        Profile = profile;
        Recording = recording;
        Earlier = earlier;
    }

    public EncodeJob? Job { get; }

    public EncodeQueueRefusal? Refusal { get; }

    public EncodeDestination? Destination { get; }

    public EncodeProfileId? ProfileId { get; }

    public EncodeProfile? Profile { get; }

    public Recording? Recording { get; }

    public EncodeJob? Earlier { get; }

    internal static EncodeQueueing Queued(EncodeJob job) => new(job, null, null, null, null, null, null);

    internal static EncodeQueueing Refused(
        EncodeQueueRefusal refusal,
        EncodeDestination? destination = null,
        EncodeProfileId? profileId = null,
        EncodeProfile? profile = null,
        Recording? recording = null,
        EncodeJob? earlier = null)
        => new(null, refusal, destination, profileId, profile, recording, earlier);
}

/// <summary>
/// Decides whether a recording asked for by hand is queued, reading the destination, the profile, the recording and
/// the jobs it already has. A retired destination or profile takes nothing new. A recording still being written, or
/// one that failed, is refused. A recording with a job waiting or running is not queued twice, and one with a
/// completed job, whatever its profile, is refused unless its artefact is asked to be made again: a recording has
/// one artefact at most.
/// </summary>
public sealed class EncodeQueueDomainService(
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IEncodeDestinationRepository destinations,
    IRecordingDirectory recordings)
{
    /// <summary>
    /// The job to queue at <paramref name="at"/>, which is handed back to be added, or why there is none.
    /// </summary>
    public async Task<EncodeQueueing> QueueAsync(EncodeJobAsked asked, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asked);

        if (await destinations.FindAsync(asked.DestinationId, cancellationToken) is not { } destination)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.NoSuchDestination);
        }

        if (destination.IsRetired)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.DestinationRetired, destination);
        }

        EncodeProfileId profileId = asked.ProfileId ?? destination.DefaultProfileId;

        if (await profiles.FindAsync(profileId, cancellationToken) is not { } profile)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.NoSuchProfile, destination, profileId);
        }

        if (profile.IsRetired)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.ProfileRetired, destination, profileId, profile);
        }

        if (await recordings.FindAsync(asked.RecordingId, cancellationToken) is not { } recording)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.NoSuchRecording, destination, profileId, profile);
        }

        if (Unencodable(recording) is { } unencodable)
        {
            return EncodeQueueing.Refused(unencodable, destination, profileId, profile, recording);
        }

        IReadOnlyList<EncodeJob> earlier = await jobs.ListForRecordingAsync(recording.Id, cancellationToken);

        if (earlier.FirstOrDefault(job => !job.HasEnded) is { } underway)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.AlreadyInTheQueue, destination, profileId, profile, recording, underway);
        }

        if (!asked.MakeItAgain && earlier.LastOrDefault(job => job.Status is EncodeJobStatus.Completed) is { } made)
        {
            return EncodeQueueing.Refused(EncodeQueueRefusal.AlreadyEncoded, destination, profileId, profile, recording, made);
        }

        return EncodeQueueing.Queued(Queued(asked.MakeItAgain, recording, profile, destination, at));
    }

    private static EncodeQueueRefusal? Unencodable(Recording recording) => recording.Encodability switch
    {
        RecordingEncodability.StillBeingWritten => EncodeQueueRefusal.RecordingStillBeingWritten,
        RecordingEncodability.Failed => EncodeQueueRefusal.RecordingFailed,
        _ => null,
    };

    private static EncodeJob Queued(
        bool makeItAgain,
        Recording recording,
        EncodeProfile profile,
        EncodeDestination destination,
        DateTime at)
        => makeItAgain
            ? EncodeJob.QueueAgain(EncodeJobId.New(), recording.Id, profile.Id, destination.Id, destination.OutputRoot, at)
            : EncodeJob.Queue(EncodeJobId.New(), recording.Id, profile.Id, destination.Id, destination.OutputRoot, at);
}
