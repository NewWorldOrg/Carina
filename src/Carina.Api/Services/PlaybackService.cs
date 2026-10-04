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
    EncodeJobId? Artefact,
    IReadOnlyList<PlaybackSource> ExternalPlayerSources);

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
        PlaybackAudience audience,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(audience);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return ServiceResult<PlaybackOffer, PlaybackFailure>.Failure(
                $"There is no recording {id.Wire}.",
                PlaybackFailure.NoSuchRecording);
        }

        PlaybackFileSearch onDisk = files.Find(recording.OutputRoot, recording.FileName);
        var announced = new AnnouncedSound(recording.SnapshotAudio, recording.SnapshotSounds);
        EncodedArtefacts encoded = await EncodedAsync(id, cancellationToken);
        IReadOnlyList<PlaybackFileSearch> playable = encoded.PlayedBy(audience);
        IReadOnlyList<PlaybackFileSearch> handedToPlayers = audience == PlaybackAudience.ExternalPlayer
            ? playable
            : encoded.PlayedBy(PlaybackAudience.ExternalPlayer);
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(recording.Outcome, onDisk, playable),
            wanted,
            SoundArrangement.Of(announced),
            from);
        IReadOnlyList<PlaybackSource> external = PlaybackPlan.SourcesHandedOver(
            new PlaybackSubject(recording.Outcome, onDisk, handedToPlayers));

        foreach (EncodedArtefact left in encoded.LeftOutBy(audience))
        {
            logger.LogInformation(
                "The artefact job {Job} made of recording {Recording} is not one {Audience} plays as it is "
                + "({Codec}), so it is left out of what playback is offered.",
                left.Job.Id.Wire,
                id.Wire,
                audience.IsBrowser ? "this browser" : "an external player",
                left.Reading.Note);
        }

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
                encoded.Made(plan),
                external))
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
        List<EncodedArtefact> found = [];

        foreach (EncodeJob job in made)
        {
            EncodeProfile? asked = defined.FirstOrDefault(profile => profile.Id.Equals(job.ProfileId));
            RecordingFileName artefact = new(job.ArtefactName!.Value);
            PlaybackFileSearch onDisk = files.Find(job.OutputRoot, artefact);

            if (asked is null)
            {
                logger.LogInformation(
                    "The artefact job {Job} made of recording {Recording} names a profile that is not defined, "
                    + "so it is left out of what playback is offered.",
                    job.Id.Wire,
                    id.Wire);

                continue;
            }

            found.Add(new EncodedArtefact(
                job,
                new ArtefactOnDisk(job.OutputRoot, artefact),
                asked.Codec,
                onDisk,
                await ReadAsync(job, onDisk, cancellationToken)));
        }

        return new EncodedArtefacts(found);
    }

    private async Task<ArtefactCodecReading> ReadAsync(
        EncodeJob job,
        PlaybackFileSearch onDisk,
        CancellationToken cancellationToken)
    {
        if (onDisk.Found is not { HoldsAnything: true } file)
        {
            return ArtefactCodecReading.Unread("the artefact is not on the disk, or holds nothing there");
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

        return reading;
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

    private sealed record EncodedArtefact(
        EncodeJob Job,
        ArtefactOnDisk Placed,
        EncodeCodec ProfileSays,
        PlaybackFileSearch OnDisk,
        ArtefactCodecReading Reading);

    private sealed record EncodedArtefacts(IReadOnlyList<EncodedArtefact> Found)
    {
        public static readonly EncodedArtefacts None = new([]);

        public IReadOnlyList<PlaybackFileSearch> PlayedBy(PlaybackAudience audience)
            => [.. Found.Where(artefact => audience.Plays(artefact.Reading, artefact.ProfileSays)).Select(artefact => artefact.OnDisk)];

        public IEnumerable<EncodedArtefact> LeftOutBy(PlaybackAudience audience)
            => Found.Where(artefact => !audience.Plays(artefact.Reading, artefact.ProfileSays));

        public EncodeJobId? Made(PlaybackPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (plan is not { Route: PlaybackRoute.Direct, Handover: { } handover })
            {
                return null;
            }

            ArtefactOnDisk handed = new(handover.Root, handover.Name);

            return Found.FirstOrDefault(artefact => artefact.Placed == handed)?.Job.Id;
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
