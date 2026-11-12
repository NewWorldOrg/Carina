using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Playback;

public sealed class PlaybackArtefactDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Queued = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 4, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Shelf = new("shelf");

    private readonly HeldEncodeJobs jobs = new();

    private readonly HeldEncodeProfiles profiles = new();

    private PlaybackArtefactDomainService Service => new(jobs, profiles);

    [Fact(DisplayName = "BR-PD-009: a recording is played from the artefacts that stand as its own, the one that ended last first and then the one queued last, with the codec their profile names")]
    public async Task ARecordingIsPlayedFromItsStandingArtefactsNewestFirst()
    {
        RecordingId recording = RecordingId.New();
        EncodeProfile h264 = Profile(EncodeCodec.H264);
        EncodeProfile h265 = Profile(EncodeCodec.H265);
        profiles.Profiles.AddRange([h264, h265]);
        EncodeJob earlier = Standing(recording, h264, Ended, Queued);
        EncodeJob queuedFirst = Standing(recording, h265, Ended.AddHours(1), Queued);
        EncodeJob queuedLast = Standing(recording, h264, Ended.AddHours(1), Queued.AddMinutes(5));
        jobs.Jobs.AddRange([earlier, queuedFirst, queuedLast]);

        PlaybackArtefacts offered = await Service.OfferedAsync(recording, Cancel);

        Assert.Equal(
            [
                new PlaybackArtefact(queuedLast, EncodeCodec.H264),
                new PlaybackArtefact(queuedFirst, EncodeCodec.H265),
                new PlaybackArtefact(earlier, EncodeCodec.H264),
            ],
            offered.Offered);
        Assert.Empty(offered.WithoutAProfile);
    }

    [Fact(DisplayName = "BR-PD-009: a job that did not complete, made nothing, was replaced or was made of another recording is not played from")]
    public async Task OnlyAnArtefactThatStandsAsTheRecordingsIsPlayedFrom()
    {
        RecordingId recording = RecordingId.New();
        EncodeProfile profile = Profile(EncodeCodec.H264);
        profiles.Profiles.Add(profile);
        EncodeJob standing = Standing(recording, profile, Ended, Queued);
        EncodeJob replaced = Standing(recording, profile, Ended.AddMinutes(-30), Queued);
        replaced.Replaced(standing, Ended.AddHours(1));
        jobs.Jobs.AddRange(
        [
            standing,
            replaced,
            Rehydrated(recording, profile, EncodeJobStatus.Failed, EncodeFileName.Artefact(recording, profile.Id)),
            Rehydrated(recording, profile, EncodeJobStatus.Completed, null),
            Standing(RecordingId.New(), profile, Ended.AddHours(2), Queued),
        ]);

        PlaybackArtefacts offered = await Service.OfferedAsync(recording, Cancel);

        Assert.Equal([new PlaybackArtefact(standing, EncodeCodec.H264)], offered.Offered);
        Assert.Empty(offered.WithoutAProfile);
    }

    [Fact(DisplayName = "BR-PD-009: an artefact whose profile is not defined is left out, and said to be")]
    public async Task AnArtefactWhoseProfileIsNotDefinedIsLeftOut()
    {
        RecordingId recording = RecordingId.New();
        EncodeProfile defined = Profile(EncodeCodec.H265);
        profiles.Profiles.Add(defined);
        EncodeJob orphaned = Standing(recording, Profile(EncodeCodec.H264), Ended.AddHours(1), Queued);
        EncodeJob kept = Standing(recording, defined, Ended, Queued);
        jobs.Jobs.AddRange([orphaned, kept]);

        PlaybackArtefacts offered = await Service.OfferedAsync(recording, Cancel);

        Assert.Equal([new PlaybackArtefact(kept, EncodeCodec.H265)], offered.Offered);
        Assert.Equal([orphaned], offered.WithoutAProfile);
    }

    [Fact(DisplayName = "BR-PD-009: a recording with no artefact standing is played from nothing encoded")]
    public async Task ARecordingWithNoArtefactIsPlayedFromNothingEncoded()
    {
        profiles.Profiles.Add(Profile(EncodeCodec.H264));

        PlaybackArtefacts offered = await Service.OfferedAsync(RecordingId.New(), Cancel);

        Assert.Empty(offered.Offered);
        Assert.Empty(offered.WithoutAProfile);
    }

    private static EncodeProfile Profile(EncodeCodec codec)
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            codec,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            Queued.AddDays(-10));

    private static EncodeJob Standing(RecordingId recording, EncodeProfile profile, DateTime ended, DateTime queued)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile.Id,
            EncodeDestinationId.New(),
            Shelf,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            queued,
            queued.AddSeconds(5),
            ended,
            null,
            EncodeFileName.Artefact(recording, profile.Id),
            null,
            null,
            null,
            null,
            null);

    private static EncodeJob Rehydrated(
        RecordingId recording,
        EncodeProfile profile,
        EncodeJobStatus status,
        EncodeFileName? artefact)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile.Id,
            EncodeDestinationId.New(),
            Shelf,
            status,
            EncodeJob.FirstAttempt,
            Queued,
            Queued.AddSeconds(5),
            Ended.AddHours(3),
            status is EncodeJobStatus.Failed ? new EncodeFailureDetail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended.AddHours(3)) : null,
            artefact,
            null,
            null,
            null,
            null,
            null);
}
