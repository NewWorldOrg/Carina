using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ChapterRewriteWorklistTests(RepositoryDatabase database) : IAsyncLifetime, IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Detected = EncodeHarness.Queued.AddHours(1);

    private static readonly DateTime Now = EncodeHarness.Queued.AddDays(2);

    private static readonly EncodeTimeline Timeline = new(TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5));

    private readonly EncodeHarness harness = new();

    public async Task InitializeAsync()
    {
        await ClearAsync();

        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at)
            VALUES (1, 'Active', false, 2, '{{}}', '{{}}', '{{}}', '{{}}', '{{}}', now(), now())
            ON CONFLICT DO NOTHING
            """,
            Cancel);
    }

    public Task DisposeAsync() => ClearAsync();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "BR-CD-010: an artefact awaits a comparison when its recording was detected by the version in use after its chapters were decided, or its chapters came from nothing or from another version")]
    public async Task AnArtefactAwaitsAComparisonWhenItsChaptersMayNoLongerBeTheDetections()
    {
        EncodeJob byTheReader = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));
        EncodeJob unread = await StandingAsync(null);
        EncodeJob byAnotherVersion = await StandingAsync(Segments(2, Detected.AddHours(1)));
        EncodeJob since = await StandingAsync(Segments(1, Detected.AddHours(1)));
        EncodeJob before = await StandingAsync(Segments(1, Detected.AddMinutes(-1)));
        EncodeJob notDetected = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started), detected: false);
        EncodeJob noTimeline = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started), timed: false);

        IReadOnlyList<ChapterRewriteSubject> awaiting = await AwaitingAsync([]);

        Assert.Equal(
            new[] { byTheReader.Id, unread.Id, byAnotherVersion.Id, before.Id }.Select(id => id.Wire).Order(StringComparer.Ordinal),
            awaiting.Select(subject => subject.Job.Id.Wire).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(awaiting, subject => new[] { since.Id, notDetected.Id, noTimeline.Id }.Contains(subject.Job.Id));
        Assert.All(awaiting, subject => Assert.Equal((subject.Job.RecordingId, SegmentModelVersion.FirstNumber), (subject.Detection!.RecordingId, subject.Detection.ModelVersion)));
        Assert.All(awaiting, subject => Assert.Null(subject.Answer));
        Assert.All(awaiting, subject => Assert.Null(subject.LastRewrittenAt));
        Assert.Single(await AwaitingAsync([], atMost: 1));
        Assert.DoesNotContain(await AwaitingAsync([unread.Id]), subject => subject.Job.Id.Equals(unread.Id));
    }

    [Fact(DisplayName = "BR-CS-002: an artefact awaits a comparison when an answer stands that its chapters were not decided from, or the answer they were decided from was withdrawn, whether or not the version in use detected the recording")]
    public async Task AnArtefactAwaitsAComparisonWhenTheAnswerChanged()
    {
        DateTime answeredAt = Detected.AddHours(2);
        EncodeJob fromTheDetection = await StandingAsync(Segments(1, Detected.AddHours(1)));
        EncodeJob fromThisAnswer = await StandingAsync(FromAnAnswer(answeredAt));
        EncodeJob fromAnEarlierAnswer = await StandingAsync(FromAnAnswer(answeredAt.AddMinutes(-5)));
        EncodeJob withdrawn = await StandingAsync(FromAnAnswer(answeredAt));
        EncodeJob withdrawnUndetected = await StandingAsync(FromAnAnswer(answeredAt), detected: false);
        EncodeJob answeredUndetected = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started), detected: false);
        EncodeJob unanswered = await StandingAsync(Segments(1, Detected.AddHours(1)));
        await AnsweredAsync(fromTheDetection, answeredAt);
        await AnsweredAsync(fromThisAnswer, answeredAt);
        await AnsweredAsync(fromAnEarlierAnswer, answeredAt);
        await AnsweredAsync(withdrawn, answeredAt, withdrawnAt: answeredAt.AddHours(1));
        await AnsweredAsync(withdrawnUndetected, answeredAt, withdrawnAt: answeredAt.AddHours(1));
        await AnsweredAsync(answeredUndetected, answeredAt);

        IReadOnlyList<ChapterRewriteSubject> awaiting = await AwaitingAsync([]);

        Assert.Equal(
            new[] { fromTheDetection.Id, fromAnEarlierAnswer.Id, withdrawn.Id, withdrawnUndetected.Id, answeredUndetected.Id }.Select(id => id.Wire).Order(StringComparer.Ordinal),
            awaiting.Select(subject => subject.Job.Id.Wire).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(awaiting, subject => new[] { fromThisAnswer.Id, unanswered.Id }.Contains(subject.Job.Id));
        ChapterRewriteSubject answeredOverTheDetection = Assert.Single(awaiting, subject => subject.Job.Id.Equals(fromTheDetection.Id));
        Assert.Equal((SegmentSource.Answered, answeredAt), (answeredOverTheDetection.Standing.Source, answeredOverTheDetection.Answer!.AnsweredAt));
        Assert.NotNull(answeredOverTheDetection.Detection);
        Assert.Equal(SegmentSource.Detected, Assert.Single(awaiting, subject => subject.Job.Id.Equals(withdrawn.Id)).Standing.Source);
        ChapterRewriteSubject nothingLeft = Assert.Single(awaiting, subject => subject.Job.Id.Equals(withdrawnUndetected.Id));
        Assert.Equal((SegmentSource.None, false), (nothingLeft.Standing.Source, nothingLeft.Answer!.Stands));
        Assert.Null(nothingLeft.Detection);
        Assert.Equal(SegmentSource.Answered, Assert.Single(awaiting, subject => subject.Job.Id.Equals(answeredUndetected.Id)).Standing.Source);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact written again within the day is left out, and one written again before that says when")]
    public async Task AnArtefactWrittenAgainWithinTheDayIsLeftOut()
    {
        EncodeJob today = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));
        EncodeJob yesterday = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));
        EncodeJob abandoned = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));
        await RewrittenAsync(today, Now.AddHours(-1), EncodeScratchFate.BecameTheArtefact);
        await RewrittenAsync(yesterday, Now.AddDays(-1).AddMinutes(-1), EncodeScratchFate.BecameTheArtefact);
        await RewrittenAsync(abandoned, Now.AddHours(-1), EncodeScratchFate.Removed);

        IReadOnlyList<ChapterRewriteSubject> awaiting = await AwaitingAsync([]);

        Assert.DoesNotContain(awaiting, subject => subject.Job.Id.Equals(today.Id));
        Assert.Equal(Now.AddDays(-1).AddMinutes(-1), Assert.Single(awaiting, subject => subject.Job.Id.Equals(yesterday.Id)).LastRewrittenAt);
        Assert.Null(Assert.Single(awaiting, subject => subject.Job.Id.Equals(abandoned.Id)).LastRewrittenAt);
    }

    [Fact(DisplayName = "BR-CD-010: a recording a job of which is waiting or running is left until it ends, and a replaced artefact is never compared")]
    public async Task ARecordingWithAJobInHandIsLeftAlone()
    {
        EncodeJob standing = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));
        EncodeJob replaced = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));

        await using (CarinaDbContext writing = database.Open())
        {
            EncodeJob held = await writing.Set<EncodeJob>().AsNoTracking().FirstAsync(job => job.Id == standing.Id, Cancel);
            await new EncodeJobRepository(writing).AddAsync(
                EncodeJob.Queue(EncodeJobId.New(), standing.RecordingId, held.ProfileId, held.DestinationId, EncodeHarness.Encodes, EncodeHarness.Queued),
                Cancel);
            await writing.Set<EncodeJob>()
                .Where(job => job.Id == replaced.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(job => job.ReplacedAt, EncodeHarness.Queued.AddHours(3)), Cancel);
        }

        Assert.Empty(await AwaitingAsync([]));
    }

    [Fact(DisplayName = "BR-CD-010: what the chapters of a standing artefact stand for is kept with the version and the answer it came from")]
    public async Task WhatTheChaptersStandForIsKept()
    {
        EncodeJob standing = await StandingAsync(Read(ChapterDetectorName.Ffmpeg, EncodeHarness.Started));

        await using (CarinaDbContext context = database.Open())
        {
            EncodeJobRepository jobs = new(context);
            EncodeJob held = (await jobs.FindAsync(standing.Id, Cancel))!;
            held.Rechaptered(new ChapterReading(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.2, Now, 1, Now.AddHours(-2)));
            await jobs.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        EncodeJob read = (await new EncodeJobRepository(reading).FindAsync(standing.Id, Cancel))!;

        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.2, Now, (int?)1, (DateTime?)Now.AddHours(-2)),
            (read.Chapters!.Detector, read.Chapters.Verdict, read.Chapters.BreakShare, read.Chapters.DecidedAt, read.Chapters.ModelVersion, read.Chapters.AnsweredAt));
    }

    private static ChapterReading Read(ChapterDetectorName detector, DateTime at) => new(detector, ChapterVerdict.Marked, 0.1, at);

    private static ChapterReading Segments(int version, DateTime at) => new(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.1, at, version);

    private static ChapterReading FromAnAnswer(DateTime answeredAt) => new(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.1, answeredAt, null, answeredAt);

    private async Task AnsweredAsync(EncodeJob job, DateTime at, DateTime? withdrawnAt = null)
    {
        RecordingAnswer answer = RecordingAnswer.Rehydrate(
            job.RecordingId,
            [
                new SegmentSpan(TimeSpan.Zero, TimeSpan.FromSeconds(600), SegmentKind.Programme, null),
                new SegmentSpan(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), SegmentKind.Break, null),
                new SegmentSpan(TimeSpan.FromSeconds(690), TimeSpan.FromMinutes(30), SegmentKind.Programme, null),
            ],
            TimeSpan.FromMinutes(30),
            false,
            null,
            at,
            1,
            null);

        await using CarinaDbContext writing = database.Open();
        SegmentAnswerRepository answers = new(writing);
        await answers.AnswerAsync(answer, Cancel);

        if (withdrawnAt is { } withdrawing)
        {
            Assert.True(await answers.WithdrawAsync(job.RecordingId, withdrawing, Cancel));
        }
    }

    private async Task<IReadOnlyList<ChapterRewriteSubject>> AwaitingAsync(IReadOnlyCollection<EncodeJobId> passedOver, int atMost = 64)
    {
        await using CarinaDbContext context = database.Open();

        return await new ChapterRewriteWorklist(context).AwaitingAsync(SegmentModelVersion.FirstNumber, Now - ChapterRewrite.OncePer, passedOver, atMost, Cancel);
    }

    private async Task RewrittenAsync(EncodeJob job, DateTime at, EncodeScratchFate fate)
    {
        EncodeScratchFile copy = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.ChapteredWork,
            EncodeHarness.Encodes,
            EncodeFileName.Chaptered(job.RecordingId, job.Id, job.Attempt, EncodeScratchFileId.New()),
            at.AddMinutes(-1));
        copy.Settle(fate, at);

        await using CarinaDbContext writing = database.Open();
        writing.Add(copy);
        await writing.SaveChangesAsync(Cancel);
    }

    private async Task<EncodeJob> StandingAsync(ChapterReading? chapters, bool detected = true, bool timed = true)
    {
        Recording recording = harness.Recorded();
        EncodeProfile profile = harness.Defined();
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            EncodeHarness.Encodes,
            profile.Id,
            EncodeHarness.Queued);
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording.Id,
            profile.Id,
            destination.Id,
            EncodeHarness.Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            EncodeHarness.Started.AddMinutes(30),
            null,
            EncodeFileName.Artefact(recording.Id, profile.Id),
            null,
            null,
            null,
            timed ? Timeline : null,
            chapters);

        await using CarinaDbContext writing = database.Open();
        await new RecordingRepository(writing).AddAsync(recording, Cancel);
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);
        await new EncodeJobRepository(writing).AddAsync(job, Cancel);

        if (detected)
        {
            await new RecordingDetectionRepository(writing).KeepAsync(
                RecordingDetection.Rehydrate(
                    recording.Id,
                    SegmentModelVersion.FirstNumber,
                    SegmentDetectionState.Marked,
                    [
                        new SegmentSpan(TimeSpan.Zero, TimeSpan.FromSeconds(300), SegmentKind.Programme, 0.9),
                        new SegmentSpan(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), SegmentKind.Break, 0.9),
                        new SegmentSpan(TimeSpan.FromSeconds(360), TimeSpan.FromMinutes(30), SegmentKind.Programme, 0.9),
                    ],
                    1 / 30d,
                    [],
                    2,
                    Detected,
                    Detected),
                Cancel);
        }

        return job;
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<RecordingDetection>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<RecordingAnswer>().ExecuteDeleteAsync(Cancel);
    }
}
