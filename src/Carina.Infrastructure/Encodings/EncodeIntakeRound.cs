using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

using Puppis.Contracts;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeIntake(
    int Waiting,
    int Queued,
    EncodeUnaskedStanding Standing,
    bool Automatically);

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
