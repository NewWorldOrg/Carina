using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ArtefactCaptionTracksTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime MadeAt = new(2026, 9, 5, 3, 30, 0, DateTimeKind.Utc);

    private static readonly TimeSpan SourceStart = TimeSpan.FromSeconds(30499.5);

    private readonly EncodeHarness harness = new();

    private readonly HeldCaptionTrackWorklist worklist = new();

    private readonly HandTurnedClock clock;

    private readonly ArtefactOpenings reads;

    public ArtefactCaptionTracksTests()
    {
        clock = new HandTurnedClock(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero));
        reads = new ArtefactOpenings(clock);
    }

    public void Dispose() => harness.Dispose();

    [Fact]
    public async Task BrEd2019NothingIsReadOnceItIsBusy()
    {
        Awaiting(Spoken());

        ArtefactCaptioningRound round = await Tracks().CaptionAsync(4, _ => Task.FromResult(true), Cancel);

        Assert.True(round.Yielded);
        Assert.Null(worklist.Subjects.Single().Job.CaptionTrack);
    }

    [Fact]
    public async Task BrEd2019AnArtefactOpenedInTheLastHalfHourIsPassedOverAndTriedLater()
    {
        EncodeJob job = Awaiting(Spoken());
        reads.Opened(job.OutputRoot, job.ArtefactName!.Value);
        clock.Turn(ArtefactCaptionTracks.LeftAloneAfterAnOpening - TimeSpan.FromSeconds(1));

        ArtefactCaptioningRound round = await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Null(job.CaptionTrack);
        Assert.Equal((1, 0, 0, 0), (round.Read, round.Added, round.Withheld, round.Failed));
        Assert.Empty(harness.Scratch.Files);
    }

    [Fact]
    public async Task BrEd2019ARecordKeptBeforeItsTextWasTakenIsWaitedForRatherThanCountedAgainstTheArtefact()
    {
        EncodeJob job = Awaiting(new CaptionRecord(1440, 1080, SourceStart, [new CaptionCue(1, null)]));

        await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Null(job.CaptionTrack);
    }

    [Fact]
    public async Task BrEd2019AnArtefactThatIsNotWhereTheLedgerSaysIsLeftAlone()
    {
        EncodeJob job = Awaiting(Spoken());
        File.Delete(harness.ArtefactPathOf(job));

        await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Null(job.CaptionTrack);
    }

    [Fact]
    public async Task BrEd2019ARecordTakenFromAFileWhoseClockBeganElsewhereGivesNoTrack()
    {
        CaptionRecord spoken = Spoken();
        EncodeJob job = Awaiting(new CaptionRecord(1440, 1080, SourceStart + TimeSpan.FromMilliseconds(2), spoken.Cues, spoken.Lines));

        ArtefactCaptioningRound round = await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Equal((EncodeCaptionTrack.Withheld, MadeAt), (job.CaptionTrack!.Value, job.CaptionTrackFrom!.Value));
        Assert.Equal(1, round.Withheld);
        Assert.Empty(harness.Scratch.Files);
    }

    [Fact]
    public async Task BrEd2019ARecordWithNoTextWithinTheArtefactGivesNoTrack()
    {
        EncodeJob job = Awaiting(new CaptionRecord(1440, 1080, SourceStart, [], []));

        await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Equal(EncodeCaptionTrack.Withheld, job.CaptionTrack);
    }

    [Fact]
    public async Task BrEd2019ACopyThatCannotBeMadeIsAFailureAndTheArtefactIsLeftAsItWas()
    {
        EncodeJob job = Awaiting(Spoken());
        byte[] before = File.ReadAllBytes(harness.ArtefactPathOf(job));

        ArtefactCaptioningRound round = await Tracks().CaptionAsync(4, NotBusy, Cancel);

        Assert.Equal((EncodeCaptionTrack.Failed, 1), (job.CaptionTrack!.Value, job.CaptionTrackAttempts));
        Assert.Equal(1, round.Failed);
        Assert.Equal(before, File.ReadAllBytes(harness.ArtefactPathOf(job)));
        Assert.DoesNotContain(Directory.EnumerateFiles(harness.WorkDirectory), file => !file.EndsWith(EncodeFileName.ArtefactExtension, StringComparison.Ordinal));
    }

    private static Task<bool> NotBusy(CancellationToken cancellationToken) => Task.FromResult(false);

    private static CaptionRecord Spoken()
        => new(
            1440,
            1080,
            SourceStart,
            [new CaptionCue((long)((SourceStart.TotalSeconds + 2) * 90_000), null)],
            [new CaptionLine((long)((SourceStart.TotalSeconds + 2) * 90_000), "合成字幕")]);

    private EncodeJob Awaiting(CaptionRecord record)
    {
        Recording recording = harness.Recorded();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording.Id,
            profile,
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            EncodeHarness.Started.AddMinutes(30),
            null,
            EncodeFileName.Artefact(recording.Id, profile),
            null,
            null,
            null,
            new EncodeTimeline(SourceStart, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)),
            null);

        harness.Jobs.Jobs.Add(job);
        File.WriteAllBytes(harness.ArtefactPathOf(job), [1, 2, 3, 4]);
        harness.CaptionShelf.KeepAsync(recording.Id, record, Cancel).GetAwaiter().GetResult();
        worklist.Subjects.Add(new CaptionTrackSubject(job, MadeAt));

        return job;
    }

    private ArtefactCaptionTracks Tracks()
        => new(
            worklist,
            harness.Jobs,
            harness.CaptionShelf,
            harness.CaptionTracks,
            harness.Places,
            harness.Placer,
            harness.Cleaner,
            reads,
            clock,
            NullLogger<ArtefactCaptionTracks>.Instance);

    private sealed class HeldCaptionTrackWorklist : ICaptionTrackWorklist
    {
        public List<CaptionTrackSubject> Subjects { get; } = [];

        public Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CaptionTrackSubject>>([.. Subjects.Where(subject => subject.Job.AwaitsCaptionTrack(subject.CaptionsMadeAt)).Take(atMost)]);

        public Task<bool> StandsWithNothingInHandAsync(EncodeJob job, CancellationToken cancellationToken)
            => Task.FromResult(job.StandsAsTheArtefact);
    }
}
