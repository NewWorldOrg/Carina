using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Services;

public enum PlaybackFailure
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    NothingWasWritten = 3,

    FileOutOfReach = 4,

    FileGone = 5,
}

public sealed record PlaybackOffer(
    PlaybackPlan Plan,
    PlaybackFile Handover,
    ServiceId Service,
    AnnouncedSound Announced,
    EncodeJobId? Artefact);

public sealed class PlaybackService(
    IRecordingDirectory recordings,
    IPlaybackFileStore files,
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IArtefactOpenings reads,
    IArtefactCodecReader codecs,
    ILogger<PlaybackService> logger)
{
    public async Task<ServiceResult<PlaybackOffer, PlaybackFailure>> OfferAsync(
        RecordingId id,
        SoundTrack wanted,
        PlaybackSource from,
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
        var announced = new AnnouncedSound(recording.SnapshotAudio, recording.SnapshotSounds);
        EncodedArtefacts encoded = await EncodedAsync(id, cancellationToken);
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(recording.Outcome, onDisk, encoded.BrowserReady),
            wanted,
            SoundArrangement.Of(announced),
            from);

        if (plan.FellBack is { } fellBack)
        {
            logger.LogWarning(
                "The ledger names an artefact of recording {Recording} that cannot be handed over ({Why}), "
                + "so it is transcoded while playing instead.",
                id.Wire,
                fellBack);
        }

        return plan.Handover is { } handover
            ? ServiceResult<PlaybackOffer, PlaybackFailure>.Success(new PlaybackOffer(
                plan,
                handover,
                recording.ServiceId,
                announced,
                encoded.Made(plan)))
            : Nothing(id, plan.Refusal!.Value);
    }

    public ServiceResult<Stream, PlaybackFailure> Open(PlaybackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        PlaybackFileOpening opened = files.OpenRead(file);

        if (opened.Reading is { } reading)
        {
            reads.Opened(file.Root, file.Name.Value);

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

    private async Task<EncodedArtefacts> EncodedAsync(
        RecordingId id,
        CancellationToken cancellationToken)
    {
        EncodeJob[] made =
        [
            .. (await jobs.ListForRecordingAsync(id, cancellationToken))
                .Where(job => job.StandsAsTheArtefact)
                .OrderByDescending(job => job.EndedAt)
                .ThenByDescending(job => job.QueuedAt),
        ];

        if (made.Length is 0)
        {
            return EncodedArtefacts.None;
        }

        IReadOnlyList<EncodeProfile> defined = await profiles.ListAsync(cancellationToken);
        List<PlaybackFileSearch> browserReady = [];
        Dictionary<ArtefactOnDisk, EncodeJobId> whoMadeIt = [];

        foreach (EncodeJob job in made)
        {
            EncodeProfile? asked = defined.FirstOrDefault(profile => profile.Id.Equals(job.ProfileId));
            RecordingFileName artefact = new(job.ArtefactName!.Value);
            PlaybackFileSearch onDisk = files.Find(job.OutputRoot, artefact);

            if (asked is null || !await EveryBrowserPlaysAsync(job, asked, onDisk, cancellationToken))
            {
                logger.LogInformation(
                    "The artefact job {Job} made of recording {Recording} is not one a browser plays as it is, "
                    + "so it is left out of what playback is offered.",
                    job.Id.Wire,
                    id.Wire);

                continue;
            }

            browserReady.Add(onDisk);
            whoMadeIt.TryAdd(new ArtefactOnDisk(job.OutputRoot, artefact), job.Id);
        }

        return new EncodedArtefacts(browserReady, whoMadeIt);
    }

    private async Task<bool> EveryBrowserPlaysAsync(
        EncodeJob job,
        EncodeProfile asked,
        PlaybackFileSearch onDisk,
        CancellationToken cancellationToken)
    {
        if (onDisk.Found is not { HoldsAnything: true } file)
        {
            return EncodeShapes.EveryBrowserPlays(asked.Codec);
        }

        ArtefactCodecReading reading = await codecs.ReadAsync(file, cancellationToken);

        if (!reading.Read)
        {
            logger.LogWarning(
                "The codec of the artefact job {Job} made could not be read from its file ({Why}), "
                + "so it is judged by what its profile says now.",
                job.Id.Wire,
                reading.Note);
        }

        return reading.EveryBrowserPlaysIt(asked.Codec);
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

    private readonly record struct ArtefactOnDisk(OutputRoot Root, RecordingFileName Name);

    private sealed record EncodedArtefacts(
        IReadOnlyList<PlaybackFileSearch> BrowserReady,
        IReadOnlyDictionary<ArtefactOnDisk, EncodeJobId> WhoMadeIt)
    {
        public static readonly EncodedArtefacts None = new([], new Dictionary<ArtefactOnDisk, EncodeJobId>());

        public EncodeJobId? Made(PlaybackPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            return plan is { Route: PlaybackRoute.Direct, Handover: { } handover }
                   && WhoMadeIt.TryGetValue(new ArtefactOnDisk(handover.Root, handover.Name), out EncodeJobId? made)
                ? made
                : null;
        }
    }

    private static string Said(RecordingId id, PlaybackRefusal refusal) => refusal switch
    {
        PlaybackRefusal.StillBeingWritten => $"Recording {id.Wire} is still being written, so there is no whole file to hand over.",
        PlaybackRefusal.NothingWasWritten => $"Recording {id.Wire} holds no bytes, so there is nothing to play.",
        PlaybackRefusal.FileGone => $"The file of recording {id.Wire} is no longer on the disk.",
        _ => $"The file of recording {id.Wire} is out of reach.",
    };
}
