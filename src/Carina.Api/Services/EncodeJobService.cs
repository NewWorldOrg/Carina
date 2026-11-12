using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

using Puppis.Contracts;

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
/// Queues one recording by hand, runs failed jobs again and calls one job off.
/// </summary>
/// <remarks>
/// A recording still being written, or one that failed, is refused. A recording with a job already
/// waiting or running is not queued twice, and a recording with any completed job, whatever its
/// profile, is refused unless the caller asks for the artefact to be made again: a recording has one
/// artefact at most. A job made again replaces every earlier artefact of the recording once it is
/// made, whatever their profile or destination. Calling a
/// job off writes the ledger first, then stops the programme running for it, then sweeps the files
/// the job owes a removal for.
/// </remarks>
public sealed class EncodeJobService(
    IEncodeJobRepository jobs,
    EncodeQueueDomainService queue,
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

        EncodeQueueing queueing = await queue.QueueAsync(
            new EncodeJobAsked(draft.RecordingId, draft.ProfileId, draft.DestinationId, draft.MakeItAgain),
            Now(),
            cancellationToken);

        if (queueing.Job is not { } queued)
        {
            return Refused(draft, queueing);
        }

        await jobs.AddAsync(queued, cancellationToken);

        events.Signal(AppEventName.EncodeJobs);

        return ServiceResult<EncodeJobView, EncodingFailure>.Success(
            Seen(queued, Now(), await turn.YieldsToAViewerAsync(cancellationToken)));
    }

    /// <summary>
    /// Runs failed jobs again, one id at a time and in the order asked, by queuing the recording each was made
    /// from with the profile and destination it named, through the same path a person queues one recording by
    /// hand. A job that did not fail is left as it stands, and what the queue refuses is answered as that row's
    /// outcome rather than as a refusal of the whole request.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<EncodeRerunRow>>> RerunAsync(IReadOnlyList<EncodeJobId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        List<EncodeRerunRow> rows = new(ids.Count);

        foreach (EncodeJobId id in ids)
        {
            rows.Add(await RerunAsync(id, cancellationToken));
        }

        return ServiceResult<IReadOnlyList<EncodeRerunRow>>.Success(rows);
    }

    private async Task<EncodeRerunRow> RerunAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        if (await jobs.FindAsync(id, cancellationToken) is not { } job)
        {
            return EncodeRerunRow.NoSuchJob(id);
        }

        if (!EncodeReruns.CanRunAgain(job))
        {
            return EncodeRerunRow.NotFailed(id, job);
        }

        ServiceResult<EncodeJobView, EncodingFailure> queued = await QueueAsync(
            new EncodeJobDraft(job.RecordingId, job.ProfileId, job.DestinationId, job.MakesItAgain),
            cancellationToken);

        return queued.IsSuccess
            ? EncodeRerunRow.QueuedAgain(id, queued.Data!)
            : EncodeRerunRow.NotQueued(id, queued.ErrorMessage!, queued.ErrorType);
    }

    public async Task<ServiceResult<EncodeJobView, EncodingFailure>> CancelAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await jobs.FindAsync(id, cancellationToken) is not { } job)
        {
            return Failure($"The ledger holds no job {id.Wire}.", EncodingFailure.NoSuchJob);
        }

        RunningProgramme? running = job.Programme;

        if (job.CallOff(Now()) is EncodeCallOff.AlreadyOver)
        {
            return Failure($"Job {id.Wire} already ended as {job.Status}, and cannot be called off.", EncodingFailure.AlreadyOver);
        }

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

    private static ServiceResult<EncodeJobView, EncodingFailure> Refused(EncodeJobDraft draft, EncodeQueueing queueing)
        => queueing.Refusal switch
        {
            EncodeQueueRefusal.NoSuchDestination => Failure(
                $"No destination {draft.DestinationId.Wire} is defined.",
                EncodingFailure.NoSuchDestination),
            EncodeQueueRefusal.DestinationRetired => Failure(EncodeSaying.NotOffered(queueing.Destination!), EncodingFailure.AlreadyRetired),
            EncodeQueueRefusal.NoSuchProfile => Failure($"No profile {queueing.ProfileId!.Wire} is defined.", EncodingFailure.NoSuchProfile),
            EncodeQueueRefusal.ProfileRetired => Failure(EncodeSaying.NotOffered(queueing.Profile!), EncodingFailure.AlreadyRetired),
            EncodeQueueRefusal.NoSuchRecording => Failure(
                $"The ledger holds no recording {draft.RecordingId.Wire}.",
                EncodingFailure.NoSuchRecording),
            EncodeQueueRefusal.RecordingStillBeingWritten => Failure(
                $"Recording {draft.RecordingId.Wire} is still being written, and is encoded once it has ended.",
                EncodingFailure.RecordingStillBeingWritten),
            EncodeQueueRefusal.RecordingFailed => Failure(
                $"Recording {draft.RecordingId.Wire} failed, so there is nothing to encode.",
                EncodingFailure.RecordingFailed),
            EncodeQueueRefusal.AlreadyInTheQueue => Failure(
                $"Recording {draft.RecordingId.Wire} already has job {queueing.Earlier!.Id.Wire} {Standing(queueing.Earlier)}; it is not queued twice.",
                EncodingFailure.AlreadyInTheQueue),
            EncodeQueueRefusal.AlreadyEncoded => Failure(
                $"Recording {draft.RecordingId.Wire} was already encoded with profile {queueing.Earlier!.ProfileId.Wire} by job {queueing.Earlier.Id.Wire}, and a recording has one artefact at most unless it is asked for again.",
                EncodingFailure.AlreadyEncoded),
            _ => throw new InvalidOperationException($"A job is refused for one of the reasons named, and this one came to {queueing.Refusal}."),
        };

    private static string Standing(EncodeJob job)
        => job.Status is EncodeJobStatus.Running ? "running" : "waiting";

    private static ServiceResult<EncodeJobView, EncodingFailure> Failure(string message, EncodingFailure failure)
        => ServiceResult<EncodeJobView, EncodingFailure>.Failure(message, failure);
}
