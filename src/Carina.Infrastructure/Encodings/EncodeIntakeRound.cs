using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeIntake(int Page, int LastPage, int Looked, int Queued, EncodeUnaskedStanding Standing)
{
    public bool MorePages => Page < LastPage;
}

/// <summary>
/// One look at the recording ledger for what has ended and has never been offered to the queue.
/// The ledger is read a page at a time and nothing else is asked for, so this asks for no new
/// event contract and cannot be starved by a run that takes half an hour (BR-ED2-004).
/// <para>
/// A recording that failed has nothing to encode and is left out by the question itself; one cut
/// short has a file and is queued like any other, and what says it was cut short is the recording,
/// not the job. A recording the ledger already holds any job for is passed over, whatever became
/// of that job. Where the artefact goes and what shape it takes is what the machine settles when
/// nobody asked, and a machine that cannot settle it queues nothing and says which of the three
/// things is missing.
/// </para>
/// </summary>
public sealed class EncodeIntakeRound(
    IRecordingDirectory recordings,
    IEncodeJobRepository jobs,
    IEncodeDestinationRepository destinations,
    IEncodeProfileRepository profiles,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<EncodeIntakeRound> logger)
{
    public const int PerLook = 100;

    private static readonly RecordingOutcome[] WithSomethingToEncode =
        [RecordingOutcome.Complete, RecordingOutcome.Truncated];

    public async Task<EncodeIntake> TakeAsync(int page, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);

        RecordingQuery query = RecordingQuery.For(
                null,
                null,
                RecordingSort.StartedAt,
                descending: false,
                page,
                PerLook,
                new RecordingConditions { Outcomes = WithSomethingToEncode })
            ?? throw new InvalidOperationException($"Page {page} of the recordings that have ended cannot be asked for.");

        PaginatedList<Recording> ended = await recordings.ListAsync(query, cancellationToken);
        IReadOnlySet<RecordingId> already = await jobs.WithAJobAsync(
            [.. ended.Items.Select(recording => recording.Id)],
            cancellationToken);

        Recording[] waiting = [.. ended.Items.Where(recording => !already.Contains(recording.Id))];

        if (waiting.Length is 0)
        {
            return new EncodeIntake(page, ended.LastPage, ended.Items.Count, 0, EncodeUnaskedStanding.Settled);
        }

        EncodeUnasked unasked = EncodeUnasked.Of(
            await destinations.ListAsync(cancellationToken),
            await profiles.ListAsync(cancellationToken));

        if (unasked is not { Destination: { } destination, Profile: { } profile })
        {
            logger.LogWarning(
                "{Waiting} recording(s) that have ended have never been offered to the encode queue, and this machine "
                + "cannot settle where an artefact goes without being asked: {Standing}.",
                waiting.Length,
                unasked.Standing);

            return new EncodeIntake(page, ended.LastPage, ended.Items.Count, 0, unasked.Standing);
        }

        foreach (Recording recording in waiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await jobs.AddAsync(
                EncodeJob.Queue(
                    EncodeJobId.New(),
                    recording.Id,
                    profile.Id,
                    destination.Id,
                    destination.OutputRoot,
                    clock.GetUtcNow().UtcDateTime),
                cancellationToken);
        }

        events.Signal(AppEventName.EncodeJobs);

        logger.LogInformation(
            "{Queued} recording(s) that had ended were put in the encode queue without being asked for.",
            waiting.Length);

        return new EncodeIntake(page, ended.LastPage, ended.Items.Count, waiting.Length, EncodeUnaskedStanding.Settled);
    }
}
