using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public enum PlaybackFailure
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    NothingWasWritten = 3,

    FileOutOfReach = 4,

    FileGone = 5,
}

public sealed record PlaybackOffer(PlaybackPlan Plan, PlaybackFile Handover, ServiceId Service);

public sealed class PlaybackService(
    IRecordingDirectory recordings,
    IPlaybackFileStore files,
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    ILogger<PlaybackService> logger)
{
    public async Task<ServiceResult<PlaybackOffer, PlaybackFailure>> OfferAsync(
        RecordingId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return ServiceResult<PlaybackOffer, PlaybackFailure>.Failure(
                $"There is no recording {id.Wire}.",
                PlaybackFailure.NoSuchRecording);
        }

        PlaybackFileSearch onDisk = files.Find(recording.OutputRoot, recording.FileName);
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(recording.Outcome, onDisk, await EncodedAsync(id, cancellationToken)));

        if (plan.FellBack is { } fellBack)
        {
            logger.LogWarning(
                "The ledger names an artefact of recording {Recording} that cannot be handed over ({Why}), "
                + "so it is transcoded while playing instead.",
                id.Wire,
                fellBack);
        }

        return plan.Handover is { } handover
            ? ServiceResult<PlaybackOffer, PlaybackFailure>.Success(new PlaybackOffer(plan, handover, recording.ServiceId))
            : Nothing(id, plan.Refusal!.Value);
    }

    public ServiceResult<Stream, PlaybackFailure> Open(PlaybackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        PlaybackFileOpening opened = files.OpenRead(file);

        if (opened.Reading is { } reading)
        {
            return ServiceResult<Stream, PlaybackFailure>.Success(reading);
        }

        return opened.Absence is PlaybackFileAbsence.Gone
            ? ServiceResult<Stream, PlaybackFailure>.Failure(
                "The file of this recording was taken off the disk before it was opened.",
                PlaybackFailure.FileGone)
            : ServiceResult<Stream, PlaybackFailure>.Failure(
                "The file of this recording went out of reach while it was being read.",
                PlaybackFailure.FileOutOfReach);
    }

    private async Task<IReadOnlyList<PlaybackFileSearch>> EncodedAsync(
        RecordingId id,
        CancellationToken cancellationToken)
    {
        EncodeJob[] made =
        [
            .. (await jobs.ListForRecordingAsync(id, cancellationToken))
                .Where(job => job.Status is EncodeJobStatus.Completed && job.ArtefactName is not null)
                .OrderByDescending(job => job.EndedAt)
                .ThenByDescending(job => job.QueuedAt),
        ];

        if (made.Length is 0)
        {
            return [];
        }

        IReadOnlyList<EncodeProfile> defined = await profiles.ListAsync(cancellationToken);
        List<PlaybackFileSearch> browserReady = [];

        foreach (EncodeJob job in made)
        {
            EncodeProfile? asked = defined.FirstOrDefault(profile => profile.Id.Equals(job.ProfileId));

            if (asked is null || !EncodeShapes.EveryBrowserPlays(asked.Codec))
            {
                logger.LogInformation(
                    "The artefact job {Job} made of recording {Recording} is not one a browser plays as it is, "
                    + "so it is left out of what playback is offered.",
                    job.Id.Wire,
                    id.Wire);

                continue;
            }

            browserReady.Add(files.Find(job.OutputRoot, new RecordingFileName(job.ArtefactName!.Value)));
        }

        return browserReady;
    }

    private static ServiceResult<PlaybackOffer, PlaybackFailure> Nothing(RecordingId id, PlaybackRefusal refusal)
        => ServiceResult<PlaybackOffer, PlaybackFailure>.Failure(
            Said(id, refusal),
            refusal switch
            {
                PlaybackRefusal.StillBeingWritten => PlaybackFailure.StillBeingWritten,
                PlaybackRefusal.NothingWasWritten => PlaybackFailure.NothingWasWritten,
                PlaybackRefusal.FileGone => PlaybackFailure.FileGone,
                _ => PlaybackFailure.FileOutOfReach,
            });

    private static string Said(RecordingId id, PlaybackRefusal refusal) => refusal switch
    {
        PlaybackRefusal.StillBeingWritten => $"Recording {id.Wire} is still being written, so there is no whole file to hand over.",
        PlaybackRefusal.NothingWasWritten => $"Recording {id.Wire} holds no bytes, so there is nothing to play.",
        PlaybackRefusal.FileGone => $"The file of recording {id.Wire} is no longer on the disk.",
        _ => $"The file of recording {id.Wire} is out of reach.",
    };
}
