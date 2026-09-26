using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

namespace Carina.Api.Services;

public sealed record EncodeJobDraft(
    RecordingId RecordingId,
    EncodeProfileId? ProfileId,
    EncodeDestinationId DestinationId,
    bool MakeItAgain);

/// <summary>
/// A job as read at a moment: what the ledger holds, how long the job has gone without headway and
/// whether that is a stall, and, for a waiting job, whether it is held back by the card being used
/// for someone watching.
/// </summary>
public sealed record EncodeJobView(EncodeJob Job, TimeSpan? QuietFor, bool Stalled, bool WaitingForAViewer);

/// <summary>
/// Queues one recording by hand and calls one job off.
/// </summary>
/// <remarks>
/// A recording still being written, or one that failed, is refused. A recording with a job already
/// waiting or running is not queued twice, and a recording with any completed job, whatever its
/// profile, is refused unless the caller asks for the artefact to be made again: a recording has one
/// artefact at most. A job made again replaces the artefact at its name once it is made. Calling a
/// job off writes the ledger first, then stops the programme running for it, then sweeps the files
/// the job owes a removal for.
/// </remarks>
public sealed class EncodeJobService(
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IEncodeDestinationRepository destinations,
    IRecordingDirectory recordings,
    IStrayProgrammes strays,
    EncodeScratchCleaner cleaner,
    EncodeSettings settings,
    EncodeQueueTurn turn,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<EncodeJobService> logger)
{
    public async Task<ServiceResult<PaginatedList<EncodeJobView>>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken)
    {
        PaginatedList<EncodeJob> found = await jobs.ListAsync(query, cancellationToken);
        DateTime now = Now();
        bool yielding = await turn.YieldsToAViewerAsync(cancellationToken);

        return ServiceResult<PaginatedList<EncodeJobView>>.Success(new PaginatedList<EncodeJobView>(
            [.. found.Items.Select(job => Seen(job, now, yielding))],
            found.Total,
            found.CurrentPage,
            found.PerPage));
    }

    public async Task<ServiceResult<EncodeSpells>> RecentSpellsAsync(CancellationToken cancellationToken)
        => ServiceResult<EncodeSpells>.Success(EncodeSpells.Of(
            await jobs.RecentSpellsAsync(EncodeSpells.MostLookedAt, cancellationToken)));

    public async Task<ServiceResult<EncodeJobView, EncodingFailure>> QueueAsync(EncodeJobDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await destinations.FindAsync(draft.DestinationId, cancellationToken) is not { } destination)
        {
            return Failure($"No destination {draft.DestinationId.Wire} is defined.", EncodingFailure.NoSuchDestination);
        }

        if (destination.IsRetired)
        {
            return Failure(EncodeSaying.NotOffered(destination), EncodingFailure.AlreadyRetired);
        }

        EncodeProfileId profileId = draft.ProfileId ?? destination.DefaultProfileId;

        if (await profiles.FindAsync(profileId, cancellationToken) is not { } profile)
        {
            return Failure($"No profile {profileId.Wire} is defined.", EncodingFailure.NoSuchProfile);
        }

        if (profile.IsRetired)
        {
            return Failure(EncodeSaying.NotOffered(profile), EncodingFailure.AlreadyRetired);
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

        if (!draft.MakeItAgain
            && earlier.LastOrDefault(job => job.Status is EncodeJobStatus.Completed) is { } made)
        {
            return Failure(
                $"Recording {recording.Id.Wire} was already encoded with profile {made.ProfileId.Wire} by job {made.Id.Wire}, and a recording has one artefact at most unless it is asked for again.",
                EncodingFailure.AlreadyEncoded);
        }

        EncodeJob queued = draft.MakeItAgain
            ? EncodeJob.QueueAgain(
                EncodeJobId.New(),
                recording.Id,
                profile.Id,
                destination.Id,
                destination.OutputRoot,
                Now())
            : EncodeJob.Queue(
                EncodeJobId.New(),
                recording.Id,
                profile.Id,
                destination.Id,
                destination.OutputRoot,
                Now());

        await jobs.AddAsync(queued, cancellationToken);

        events.Signal(AppEventName.EncodeJobs);

        return ServiceResult<EncodeJobView, EncodingFailure>.Success(
            Seen(queued, Now(), await turn.YieldsToAViewerAsync(cancellationToken)));
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

        events.Signal(AppEventName.EncodeJobs);

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

        return ServiceResult<EncodeJobView, EncodingFailure>.Success(
            Seen(job, Now(), await turn.YieldsToAViewerAsync(cancellationToken)));
    }

    private EncodeJobView Seen(EncodeJob job, DateTime now, bool yielding)
        => new(
            job,
            job.QuietFor(now),
            job.IsStalled(now, settings.StalledAfter),
            yielding && job.Status is EncodeJobStatus.Queued);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static string Standing(EncodeJob job)
        => job.Status is EncodeJobStatus.Running ? "running" : "waiting";

    private static ServiceResult<EncodeJobView, EncodingFailure> Failure(string message, EncodingFailure failure)
        => ServiceResult<EncodeJobView, EncodingFailure>.Failure(message, failure);
}
