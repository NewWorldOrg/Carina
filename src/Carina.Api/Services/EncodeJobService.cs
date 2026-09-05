using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

namespace Carina.Api.Services;

public sealed record EncodeJobDraft(RecordingId RecordingId, EncodeProfileId? ProfileId, EncodeDestinationId DestinationId);

/// <summary>
/// A job as read at a moment: what the ledger holds, and what the reader works out from the time
/// beside it — how long the job has gone without headway and whether that is a stall (BR-ED2-014).
/// </summary>
public sealed record EncodeJobView(EncodeJob Job, TimeSpan? QuietFor, bool Stalled);

/// <summary>
/// Puts one recording in the queue by hand and calls one job off. One job is queued at a time, for
/// one recording, so there is no way in that takes a list (BR-ED2-008). A recording still being
/// written, or one that failed, has nothing to encode; a recording with a job already waiting or
/// running is not queued twice, and one whose artefact for this profile already exists is not made
/// again, because the second would only collide with the first (BR-ED2-009). Calling a job off is a
/// person's act and is kept apart from a failure (BR-ED2-012): the ledger is written first, then the
/// programme still running for it is stopped, then what the job owes a removal for is swept.
/// </summary>
public sealed class EncodeJobService(
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IEncodeDestinationRepository destinations,
    IRecordingDirectory recordings,
    IStrayProgrammes strays,
    EncodeScratchCleaner cleaner,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeJobService> logger)
{
    public async Task<ServiceResult<PaginatedList<EncodeJobView>>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken)
    {
        PaginatedList<EncodeJob> found = await jobs.ListAsync(query, cancellationToken);
        DateTime now = Now();

        return ServiceResult<PaginatedList<EncodeJobView>>.Success(new PaginatedList<EncodeJobView>(
            [.. found.Items.Select(job => Seen(job, now))],
            found.Total,
            found.CurrentPage,
            found.PerPage));
    }

    public async Task<ServiceResult<EncodeJobView, EncodingFailure>> QueueAsync(EncodeJobDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await destinations.FindAsync(draft.DestinationId, cancellationToken) is not { } destination)
        {
            return Failure($"No destination {draft.DestinationId.Wire} is defined.", EncodingFailure.NoSuchDestination);
        }

        EncodeProfileId profileId = draft.ProfileId ?? destination.DefaultProfileId;

        if (await profiles.FindAsync(profileId, cancellationToken) is not { } profile)
        {
            return Failure($"No profile {profileId.Wire} is defined.", EncodingFailure.NoSuchProfile);
        }

        if (await recordings.FindAsync(draft.RecordingId, cancellationToken) is not { } recording)
        {
            return Failure($"The ledger holds no recording {draft.RecordingId.Wire}.", EncodingFailure.NoSuchRecording);
        }

        if (recording.IsInFlight)
        {
            return Failure(
                $"Recording {recording.Id.Wire} is still being written, and is encoded once it has ended.",
                EncodingFailure.RecordingStillBeingWritten);
        }

        if (recording.Outcome is RecordingOutcome.Failed)
        {
            return Failure(
                $"Recording {recording.Id.Wire} failed, so there is nothing to encode.",
                EncodingFailure.RecordingFailed);
        }

        IReadOnlyList<EncodeJob> earlier = await jobs.ListForRecordingAsync(recording.Id, cancellationToken);

        if (earlier.FirstOrDefault(job => !job.HasEnded) is { } underway)
        {
            return Failure(
                $"Recording {recording.Id.Wire} already has job {underway.Id.Wire} {Standing(underway)}; it is not queued twice.",
                EncodingFailure.AlreadyInTheQueue);
        }

        if (earlier.FirstOrDefault(job => job.Status is EncodeJobStatus.Completed && job.ProfileId.Equals(profile.Id)) is { } made)
        {
            return Failure(
                $"Recording {recording.Id.Wire} was already encoded with profile {profile.Id.Wire} by job {made.Id.Wire}, and a second artefact would only collide with the first.",
                EncodingFailure.AlreadyEncoded);
        }

        EncodeJob queued = EncodeJob.Queue(
            EncodeJobId.New(),
            recording.Id,
            profile.Id,
            destination.Id,
            destination.OutputRoot,
            Now());

        await jobs.AddAsync(queued, cancellationToken);

        return ServiceResult<EncodeJobView, EncodingFailure>.Success(Seen(queued, Now()));
    }

    public async Task<ServiceResult<EncodeJobView, EncodingFailure>> CancelAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await jobs.FindAsync(id, cancellationToken) is not { } job)
        {
            return Failure($"The ledger holds no job {id.Wire}.", EncodingFailure.NoSuchJob);
        }

        if (job.HasEnded)
        {
            return Failure($"Job {id.Wire} already ended as {job.Status}, and cannot be called off.", EncodingFailure.AlreadyOver);
        }

        RunningProgramme? running = job.Programme;
        job.Cancel(Now());

        try
        {
            await jobs.SaveAsync(job, cancellationToken);
        }
        catch (EncodeJobMovedMeanwhileException)
        {
            return Failure($"Job {id.Wire} moved in the ledger while it was being called off; read it again.", EncodingFailure.MovedMeanwhile);
        }

        if (running is { } programme)
        {
            StrayFate fate = strays.Stop(programme);
            logger.LogInformation(
                "Job {Job} was called off while running as process {Process}: {Fate}.",
                job.Id.Wire,
                programme.ProcessId,
                fate);
        }

        await cleaner.ClearAsync(job, cancellationToken);

        return ServiceResult<EncodeJobView, EncodingFailure>.Success(Seen(job, Now()));
    }

    private EncodeJobView Seen(EncodeJob job, DateTime now)
        => new(job, job.QuietFor(now), job.IsStalled(now, settings.StalledAfter));

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static string Standing(EncodeJob job)
        => job.Status is EncodeJobStatus.Running ? "running" : "waiting";

    private static ServiceResult<EncodeJobView, EncodingFailure> Failure(string message, EncodingFailure failure)
        => ServiceResult<EncodeJobView, EncodingFailure>.Failure(message, failure);
}
