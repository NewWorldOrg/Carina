using Carina.Api.Common;
using Carina.Domain.Channels;
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

public sealed class PlaybackService(IRecordingDirectory recordings, IPlaybackFileStore files)
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
            PlaybackSubject.NothingHasBeenEncodedYet(recording.Outcome, onDisk));

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
