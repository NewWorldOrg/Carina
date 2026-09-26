using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeIntake(
    int Waiting,
    int Queued,
    EncodeUnaskedStanding Standing,
    bool Automatically);

/// <summary>
/// One look at the recording ledger for what has ended and has never been offered to the queue.
/// The ledger is asked for those recordings and nothing else, at most a look's worth at a time, so
/// this asks for no new event contract and cannot be starved by a run that takes half an hour
/// (BR-ED2-004). What a look queues leaves the answer, so the next look reads the next ones.
/// <para>
/// A machine whose auto-run is turned off looks at nothing at all, and the answer says so, because
/// the setting is read on every look rather than at a start: turning it back on is in force at the
/// next one. A recording that failed has nothing to encode and is left out by the question itself; one cut
/// short has a file and is queued like any other, and what says it was cut short is the recording,
/// not the job. A recording the ledger already holds any job for is passed over, whatever became
/// of that job. Where the artefact goes and what shape it takes is what the machine settles when
/// nobody asked, and a machine that cannot settle it queues nothing and says which of the three
/// things is missing.
/// </para>
/// </summary>
public sealed class EncodeIntakeRound(
    IEncodeIntakeReader intake,
    IEncodeJobRepository jobs,
    IEncodeDestinationRepository destinations,
    IEncodeProfileRepository profiles,
    IEncodeAutoRunReader autoRun,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<EncodeIntakeRound> logger)
{
    public const int PerLook = 100;

    public async Task<EncodeIntake> TakeAsync(CancellationToken cancellationToken)
    {
        if (!(await autoRun.ReadAsync(cancellationToken)).Automatically)
        {
            return new EncodeIntake(0, 0, EncodeUnaskedStanding.Settled, Automatically: false);
        }

        IReadOnlyList<RecordingId> waiting = await intake.NeverQueuedAsync(PerLook, cancellationToken);

        if (waiting.Count is 0)
        {
            return new EncodeIntake(0, 0, EncodeUnaskedStanding.Settled, Automatically: true);
        }

        EncodeUnasked unasked = EncodeUnasked.Of(
            await destinations.ListAsync(cancellationToken),
            await profiles.ListAsync(cancellationToken));

        if (unasked is not { Destination: { } destination, Profile: { } profile })
        {
            logger.LogWarning(
                "{Waiting} recording(s) that have ended have never been offered to the encode queue, and this machine "
                + "cannot settle where an artefact goes without being asked: {Standing}.",
                waiting.Count,
                unasked.Standing);

            return new EncodeIntake(waiting.Count, 0, unasked.Standing, Automatically: true);
        }

        foreach (RecordingId recording in waiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await jobs.AddAsync(
                EncodeJob.Queue(
                    EncodeJobId.New(),
                    recording,
                    profile.Id,
                    destination.Id,
                    destination.OutputRoot,
                    clock.GetUtcNow().UtcDateTime),
                cancellationToken);
        }

        events.Signal(AppEventName.EncodeJobs);

        logger.LogInformation(
            "{Queued} recording(s) that had ended were put in the encode queue without being asked for.",
            waiting.Count);

        return new EncodeIntake(waiting.Count, waiting.Count, EncodeUnaskedStanding.Settled, Automatically: true);
    }
}
