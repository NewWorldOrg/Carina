using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Thumbnails;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Standing artefacts whose chapters are compared with the segments, held in memory around an
/// <see cref="EncodeHarness"/>: the worklist hands out what a test puts in it, the ledger says an artefact stands
/// with nothing in hand until a test says otherwise, and the room holds as much as a test gives it.
/// </summary>
internal sealed class ChapterRewriteHarness : IDisposable
{
    public static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5);

    public ChapterRewriteHarness(EncodeHarness? encodes = null)
    {
        Encodes = encodes ?? new EncodeHarness();
        Openings = new ArtefactOpenings(Encodes.Clock);
    }

    public EncodeHarness Encodes { get; }

    public HeldChapterRewriteWorklist Worklist { get; } = new();

    public HeldStanding Standing { get; } = new();

    public ArtefactOpenings Openings { get; }

    public HeldFreeSpace Room { get; } = new();

    public HeardOf<ArtefactChapterRewrites> Log { get; } = new();

    public ChapterRewriteMux Mux => new(Encodes.ScratchFiles, Encodes.Programmes, Encodes.Clock);

    public ArtefactChapterRewrites Rewrites => new(
        Worklist,
        Standing,
        Encodes.Jobs,
        Encodes.Chapters,
        Mux,
        Encodes.Places,
        Encodes.Placer,
        Encodes.Cleaner,
        Openings,
        Room,
        Encodes.Clock,
        Log);

    public DateTime Now => Encodes.Clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// A completed job whose artefact stands with the chapters given on its own clock, made from a recording of
    /// the length given with <see cref="HeadSkip"/> skipped; its artefact holds the bytes given.
    /// </summary>
    public EncodeJob Stood(TimeSpan recorded, IReadOnlyList<ChapterSegment> carried, byte[] artefact)
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            EncodeHarness.Started.AddMinutes(10),
            null,
            EncodeFileName.Artefact(recording, profile),
            null,
            null,
            null,
            new EncodeTimeline(TimeSpan.FromSeconds(3.2), HeadSkip, recorded, recorded - HeadSkip),
            new ChapterReading(ChapterDetectorName.Ffmpeg, carried.Count is 0 ? ChapterVerdict.NothingFound : ChapterVerdict.Marked, 0.1, EncodeHarness.Started));
        Encodes.Jobs.Jobs.Add(job);
        Encodes.Chapters.RecordAsync(job.Id, EncodeChapter.Mark(job.Id, carried), CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllBytes(Encodes.ArtefactPathOf(job), artefact);

        return job;
    }

    /// <summary>
    /// Puts the job in the worklist beside a detection of its recording by the first version, standing as told,
    /// its stretches given in the recording's own time.
    /// </summary>
    public RecordingDetection Awaiting(EncodeJob job, SegmentDetectionState state, DateTime? lastRewrittenAt, params SegmentSpan[] stretches)
    {
        double breaks = stretches.Where(stretch => stretch.Kind is SegmentKind.Break).Sum(stretch => stretch.Length.Ticks);
        RecordingDetection detection = RecordingDetection.Rehydrate(
            job.RecordingId,
            SegmentModelVersion.FirstNumber,
            state,
            stretches,
            breaks / stretches[^1].Ends.Ticks,
            [],
            2,
            Now,
            Now);
        Worklist.Subjects.Add(new ChapterRewriteSubject(job, detection, null, lastRewrittenAt));

        return detection;
    }

    /// <summary>
    /// Puts the job in the worklist beside the answer given for its recording at <paramref name="answeredAt"/>,
    /// withdrawn when that is given, and beside the detection given, if any; the answer's stretches are given in
    /// the recording's own time.
    /// </summary>
    public RecordingAnswer Answered(
        EncodeJob job,
        RecordingDetection? detection,
        DateTime answeredAt,
        DateTime? withdrawnAt,
        params SegmentSpan[] stretches)
    {
        RecordingAnswer answer = RecordingAnswer.Rehydrate(job.RecordingId, stretches, stretches[^1].Ends, true, answeredAt, answeredAt, 1, withdrawnAt);
        Worklist.Subjects.RemoveAll(subject => subject.Job.Id.Equals(job.Id));
        Worklist.Subjects.Add(new ChapterRewriteSubject(job, detection, answer, null));

        return answer;
    }

    public void Dispose() => Encodes.Dispose();
}

internal sealed class HeldChapterRewriteWorklist : IChapterRewriteWorklist
{
    public List<ChapterRewriteSubject> Subjects { get; } = [];

    public List<(int ModelVersion, DateTime RewrittenSince, IReadOnlyCollection<EncodeJobId> PassedOver, int AtMost)> Asked { get; } = [];

    public Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(
        int modelVersion,
        DateTime rewrittenSince,
        IReadOnlyCollection<EncodeJobId> passedOver,
        int atMost,
        CancellationToken cancellationToken)
    {
        Asked.Add((modelVersion, rewrittenSince, passedOver, atMost));

        return Task.FromResult<IReadOnlyList<ChapterRewriteSubject>>([.. Subjects.Where(subject => !passedOver.Contains(subject.Job.Id)).Take(atMost)]);
    }
}

internal sealed class HeldStanding : ICaptionTrackWorklist
{
    public bool WithNothingInHand { get; set; } = true;

    public Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CaptionTrackSubject>>([]);

    public Task<bool> StandsWithNothingInHandAsync(EncodeJob job, CancellationToken cancellationToken)
        => Task.FromResult(WithNothingInHand && job.StandsAsTheArtefact);
}

internal sealed class HeldFreeSpace : IFreeSpaceReader
{
    public long? Free { get; set; } = long.MaxValue;

    public long? FreeBytes(string directory) => Free;
}
