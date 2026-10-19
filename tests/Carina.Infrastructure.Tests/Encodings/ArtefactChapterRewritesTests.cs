using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ArtefactChapterRewritesTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Recorded = TimeSpan.FromMinutes(30);

    private static readonly byte[] TheArtefact = [1, 2, 3, 4];

    private static readonly IReadOnlyList<ChapterSegment> APod =
    [
        new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(299.5), ChapterKind.Programme),
        new ChapterSegment(TimeSpan.FromSeconds(299.5), TimeSpan.FromSeconds(359.5), ChapterKind.Break),
        new ChapterSegment(TimeSpan.FromSeconds(359.5), Recorded - ChapterRewriteHarness.HeadSkip, ChapterKind.Programme),
    ];

    private readonly ChapterRewriteHarness harness = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "BR-CD-010: nothing is read once the machine is needed")]
    public async Task NothingIsReadOnceTheMachineIsNeeded()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Moved());

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Needed, Cancel);

        Assert.True(round.Yielded);
        Assert.Equal(ChapterDetectorName.Ffmpeg, job.Chapters!.Detector);
        Assert.Empty(harness.Encodes.Scratch.Files);
    }

    [Fact(DisplayName = "BR-CD-010: chapters that still agree with the detection are left in the artefact and written down as standing for it")]
    public async Task ChaptersThatStillAgreeAreWrittenDownAsStandingForTheDetection()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        RecordingDetection detection = harness.Awaiting(
            job,
            SegmentDetectionState.Marked,
            null,
            Span(0, 300.4, SegmentKind.Programme),
            Span(300.4, 360, SegmentKind.Break),
            Span(360, Recorded.TotalSeconds, SegmentKind.Programme));

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal((1, 1, 0), (round.Read, round.AsCarried, round.Rewritten));
        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.Marked, 1, harness.Now),
            (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion, job.Chapters.DecidedAt));
        Assert.Equal(detection.CmShare, job.Chapters.BreakShare);
        Assert.Equal(APod, harness.Encodes.Chapters.Chapters.Select(chapter => chapter.Segment));
        Assert.Equal(TheArtefact, File.ReadAllBytes(harness.Encodes.ArtefactPathOf(job)));
        Assert.Empty(harness.Encodes.Scratch.Files);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact opened in the last half hour is passed over and tried later")]
    public async Task AnArtefactOpenedInTheLastHalfHourIsPassedOver()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Moved());
        harness.Openings.Opened(job.OutputRoot, job.ArtefactName!.Value);
        ((HandTurnedClock)harness.Encodes.Clock).Turn(ArtefactCaptionTracks.LeftAloneAfterAnOpening - TimeSpan.FromSeconds(1));

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal((1, 0, 0), (round.PassedOver, round.Rewritten, round.Failed.Count));
        Assert.Empty(harness.Encodes.Scratch.Files);
        Assert.Equal(ChapterDetectorName.Ffmpeg, job.Chapters!.Detector);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact whose room holds less than its size is not written again, and is tried at the next chance")]
    public async Task AnArtefactWithoutRoomForItsCopyIsPassedOver()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Moved());
        harness.Room.Free = TheArtefact.Length - 1;

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal((1, 0), (round.PassedOver, round.Failed.Count));
        Assert.Empty(harness.Encodes.Scratch.Files);

        harness.Room.Free = null;

        Assert.Equal(1, (await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel)).PassedOver);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact written again within the day is not written again, however its chapters moved")]
    public async Task AnArtefactWrittenAgainWithinTheDayIsPassedOver()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, harness.Now.AddHours(-23), Moved());

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal((1, 0), (round.PassedOver, round.Rewritten));
        Assert.Empty(harness.Encodes.Scratch.Files);
        Assert.Equal(harness.Now - ChapterRewrite.OncePer, harness.Worklist.Asked.Single().RewrittenSince);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact that is not where the ledger says is left alone")]
    public async Task AnArtefactThatIsNotWhereTheLedgerSaysIsLeftAlone()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Moved());
        File.Delete(harness.Encodes.ArtefactPathOf(job));

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal(1, round.PassedOver);
        Assert.Empty(harness.Encodes.Scratch.Files);
    }

    [Fact(DisplayName = "BR-CD-010: a copy that cannot be made fails the artefact for the round, leaves it as it was and sweeps what was written for it")]
    public async Task ACopyThatCannotBeMadeLeavesTheArtefactAsItWas()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Moved());
        harness.Encodes.Standing("printf 'half a copy' > \"$destination\"; exit 1");

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal([job.Id], round.Failed);
        Assert.Equal(TheArtefact, File.ReadAllBytes(harness.Encodes.ArtefactPathOf(job)));
        Assert.Equal(ChapterDetectorName.Ffmpeg, job.Chapters!.Detector);
        Assert.Equal(APod, harness.Encodes.Chapters.Chapters.Select(chapter => chapter.Segment));
        Assert.Equal([EncodeScratchKind.Chapters, EncodeScratchKind.ChapteredWork], harness.Encodes.Scratch.Files.Select(scratch => scratch.Kind));
        Assert.All(harness.Encodes.Scratch.Files, scratch => Assert.False(scratch.IsOwedARemoval));
        Assert.Equal([harness.Encodes.ArtefactPathOf(job)], Directory.EnumerateFiles(harness.Encodes.Shelf.Root));
    }

    [Fact(DisplayName = "BR-CD-010: a detection thrown away takes the chapters out of the artefact: the copy reads no chapters file and is written with none")]
    public async Task ADetectionThrownAwayTakesTheChaptersOut()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Discarded, null, Span(0, 600, SegmentKind.Programme), Span(600, Recorded.TotalSeconds, SegmentKind.Break));
        string arguments = harness.Encodes.Room.Under("arguments");
        harness.Encodes.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; exit 1");

        await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.DoesNotContain("ffmetadata", handed);
        Assert.Equal("-1", handed[Array.IndexOf(handed, "-map_chapters") + 1]);
        Assert.Equal([EncodeScratchKind.ChapteredWork], harness.Encodes.Scratch.Files.Select(scratch => scratch.Kind));
    }

    [Fact(DisplayName = "BR-CD-010: a break the detection now finds to be an opening, where it was, is what the artefact is written again with, titled OP")]
    public async Task ABreakNowFoundToBeAnOpeningIsWrittenAgain()
    {
        EncodeJob job = harness.Stood(Recorded, APod, TheArtefact);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Opening), Span(360, Recorded.TotalSeconds, SegmentKind.Programme));
        string handed = harness.Encodes.Room.Under("handed-chapters");
        harness.Encodes.Standing($"for chapters in \"$@\"; do if head -c 12 \"$chapters\" 2>/dev/null | grep -q FFMETADATA; then cp \"$chapters\" \"{handed}\"; fi; done; exit 1");

        ChapterRewriteRound round = await harness.Rewrites.RewriteAsync(1, 4, [], Spare, Cancel);

        Assert.Equal((0, 1), (round.AsCarried, round.Failed.Count));
        Assert.Contains("START=299500\nEND=359500\ntitle=OP", File.ReadAllText(handed), StringComparison.Ordinal);
        Assert.Equal(APod, harness.Encodes.Chapters.Chapters.Select(chapter => chapter.Segment));
    }

    private static Task<bool> Spare(CancellationToken cancellationToken) => Task.FromResult(true);

    private static Task<bool> Needed(CancellationToken cancellationToken) => Task.FromResult(false);

    private static SegmentSpan[] Moved()
        => [Span(0, 310, SegmentKind.Programme), Span(310, 360, SegmentKind.Break), Span(360, Recorded.TotalSeconds, SegmentKind.Programme)];

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
