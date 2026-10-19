using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Where an encode takes its chapters from: the recording's detection by the version of the model in use when
/// there is one, nothing while learning is on and there is none, and the look at the source the machine is set
/// up with while learning is off and there is none.
/// </summary>
public sealed class EncodeJobRunnerSegmentTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Skipped = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan Artefact = EncodeHarness.Whole - Skipped;

    private const string Source = "<the recording>";

    private const string Work = "<the work file>";

    private const string Metadata = "<the chapters>";

    private static ChapterDetection APodInTheMiddle => ChapterDetection.Marked(
        [
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(5), Artefact, ChapterKind.Programme),
        ],
        Artefact,
        0.3);

    [Fact(DisplayName = "BR-CD-010: a detection that marked breaks is embedded in the recording's own time, titled for the programme and the break, and the ledger keeps it came from the segments and which version, with the chapters on the artefact's clock")]
    public async Task ADetectionThatMarkedBreaksIsEmbeddedInTheRecordingsOwnTime()
    {
        foreach (bool learning in new[] { true, false })
        {
            using var harness = new EncodeHarness();
            Run run = Watched(harness);
            harness.Learning(learning);
            harness.Versions.Active = HeldSegmentModelVersions.InUse(2, HeldSegmentModelVersions.Installed);
            var looked = new ScriptedChapters { Answers = () => APodInTheMiddle };
            harness.ChapterDetector = looked;
            Recording recording = harness.Recorded();
            harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
            RecordingDetection detection = harness.Detected(
                recording,
                SegmentDetectionState.Marked,
                Span(0, 3, SegmentKind.Programme),
                Span(3, 6.25, SegmentKind.Break),
                Span(6.25, 10, SegmentKind.Programme));
            EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

            Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

            Assert.Equal(
                """
                ;FFMETADATA1
                [CHAPTER]
                TIMEBASE=1/1000
                START=0
                END=3000
                title=本編
                [CHAPTER]
                TIMEBASE=1/1000
                START=3000
                END=6250
                title=CM
                [CHAPTER]
                TIMEBASE=1/1000
                START=6250
                END=10000
                title=本編

                """.ReplaceLineEndings("\n"),
                File.ReadAllText(run.Chapters));
            Assert.Equal(
                (ChapterDetectorName.Segments, ChapterVerdict.Marked, 2),
                (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
            Assert.Equal(detection.CmShare, job.Chapters.BreakShare);
            Assert.Equal(
                [
                    (TimeSpan.Zero, TimeSpan.FromSeconds(2.5), ChapterKind.Programme),
                    (TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.75), ChapterKind.Break),
                    (TimeSpan.FromSeconds(5.75), Artefact, ChapterKind.Programme),
                ],
                harness.Chapters.Chapters.Select(chapter => (chapter.StartsAt, chapter.EndsAt, chapter.Kind)));

            string[] handed = run.Handed(harness, job);
            Assert.Equal(["-f", "ffmetadata", "-i", Metadata], handed.SkipWhile(argument => argument != Source).Skip(1).Take(4));
            Assert.Contains("-map_chapters", handed);
            Assert.Empty(looked.Asked);
            Assert.Empty(harness.Watermarks.Asked);
            Assert.Empty(harness.Watermarks.Kept);
        }
    }

    [Fact(DisplayName = "BR-CD-010: an opening and an ending the detection found are embedded titled OP and ED, and the ledger keeps them by those kinds on the artefact's clock")]
    public async Task AnOpeningAndAnEndingAreEmbeddedTitledOpAndEd()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        harness.Learning(true);
        Recording recording = harness.Recorded();
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            Span(0, 2, SegmentKind.Opening),
            Span(2, 5, SegmentKind.Programme),
            Span(5, 6, SegmentKind.Break),
            Span(6, 8.5, SegmentKind.Programme),
            Span(8.5, 10, SegmentKind.Ending));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Equal(
            ["title=OP", "title=本編", "title=CM", "title=本編", "title=ED"],
            File.ReadAllLines(run.Chapters).Where(line => line.StartsWith("title=", StringComparison.Ordinal)));
        Assert.Equal(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1.5), ChapterKind.Opening),
                (TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4.5), ChapterKind.Programme),
                (TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(5.5), ChapterKind.Break),
                (TimeSpan.FromSeconds(5.5), TimeSpan.FromSeconds(8), ChapterKind.Programme),
                (TimeSpan.FromSeconds(8), Artefact, ChapterKind.Ending),
            ],
            harness.Chapters.Chapters.Select(chapter => (chapter.StartsAt, chapter.EndsAt, chapter.Kind)));
    }

    [Fact(DisplayName = "BR-CD-010: a detection that found only the programme, or was thrown away, is encoded with the arguments of a run with no chapters, and the ledger keeps which version said so")]
    public async Task ADetectionWithNothingToEmbedIsEncodedWithNoChapters()
    {
        string[] none = await NoChaptersTodayAsync();
        (SegmentDetectionState State, SegmentSpan[] Stretches)[] nothingToEmbed =
        [
            (SegmentDetectionState.NothingFound, [Span(0, 10, SegmentKind.Programme)]),
            (SegmentDetectionState.Discarded, [Span(0, 4, SegmentKind.Programme), Span(4, 10, SegmentKind.Break)]),
        ];

        foreach ((SegmentDetectionState state, SegmentSpan[] stretches) in nothingToEmbed)
        {
            using var harness = new EncodeHarness();
            Run run = Watched(harness);
            harness.Learning(true);
            Recording recording = harness.Recorded();
            harness.Detected(recording, state, stretches);
            EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

            Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

            Assert.Equal(none, run.Handed(harness, job));
            Assert.Equal(
                (ChapterDetectorName.Segments, state is SegmentDetectionState.Discarded ? ChapterVerdict.Discarded : ChapterVerdict.NothingFound, 1),
                (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
            Assert.Empty(harness.Chapters.Chapters);
            Assert.Equal([EncodeScratchKind.WorkFile], harness.Scratch.Files.Select(scratch => scratch.Kind));
        }
    }

    [Fact(DisplayName = "BR-CD-010: with learning on and no detection the encode has no chapters, its arguments are those of a run with no chapters today, and the source is not looked at")]
    public async Task WithLearningOnAndNoDetectionTheEncodeHasNoChapters()
    {
        string[] none = await NoChaptersTodayAsync();
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        harness.Learning(true);
        var looked = new ScriptedChapters { Answers = () => APodInTheMiddle };
        harness.ChapterDetector = looked;
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Equal(none, run.Handed(harness, job));
        Assert.Empty(looked.Asked);
        Assert.Empty(harness.Watermarks.Asked);
        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.NotAsked, (int?)null),
            (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
        Assert.Empty(harness.Chapters.Chapters);
        Assert.False(File.Exists(harness.ChaptersPathOf(job)));
    }

    [Fact(DisplayName = "BR-CD-013: with learning switched off and no detection the source is looked at as it is today, and the encode is handed the same arguments as before learning was ever switched")]
    public async Task WithLearningOffAndNoDetectionTheSourceIsLookedAtAsToday()
    {
        string[] neverSwitched = await LookedAtAsync(switched: false);
        string[] switchedOff = await LookedAtAsync(switched: true);

        Assert.Equal(neverSwitched, switchedOff);
        Assert.Contains("ffmetadata", switchedOff);
        Assert.Contains("-map_chapters", switchedOff);
    }

    private static async Task<string[]> LookedAtAsync(bool switched)
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);

        if (switched)
        {
            harness.Learning(false);
        }

        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        var looked = new ScriptedChapters { Answers = () => APodInTheMiddle };
        harness.ChapterDetector = looked;
        Recording recording = harness.Recorded();
        harness.Versions.Active = HeldSegmentModelVersions.InUse(2, HeldSegmentModelVersions.Installed);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Single(looked.Asked);
        Assert.Equal(ChapterMetadataFile.Written(APodInTheMiddle.Segments, Skipped), File.ReadAllText(run.Chapters));
        Assert.Equal((ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, (int?)null), (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
        Assert.Equal(3, harness.Chapters.Chapters.Count);

        return run.Handed(harness, job);
    }

    /// <summary>
    /// The arguments of a run with no chapters as it stands without learning: nobody is asked to look.
    /// </summary>
    private static async Task<string[]> NoChaptersTodayAsync()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));
        Assert.Equal(ChapterDetectorName.Nobody, job.Chapters!.Detector);

        return run.Handed(harness, job);
    }

    private static Run Watched(EncodeHarness harness)
    {
        string arguments = harness.Room.Under("arguments");
        string chapters = harness.Room.Under("chapters-handed");
        harness.Standing(
            $"printf '%s\\n' \"$@\" > \"{arguments}\"; "
            + $"for given in \"$@\"; do case \"$given\" in *{EncodeFileName.ChaptersExtension}) cp \"$given\" \"{chapters}\";; esac; done; "
            + "printf 'the picture' > \"$destination\"");

        return new Run(arguments, chapters);
    }

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);

    /// <summary>
    /// What the programme standing in for ffmpeg was handed, and where it kept the chapters it was handed.
    /// </summary>
    private sealed record Run(string Arguments, string Chapters)
    {
        public string[] Handed(EncodeHarness harness, EncodeJob job)
            => [.. File.ReadAllLines(Arguments).Select(argument => Named(harness, job, argument))];

        private static string Named(EncodeHarness harness, EncodeJob job, string argument)
        {
            if (argument == harness.SourcePathOf(harness.Recordings.Rows[0]))
            {
                return Source;
            }

            if (argument == harness.WorkPathOf(job))
            {
                return Work;
            }

            return argument == harness.ChaptersPathOf(job) ? Metadata : argument;
        }
    }
}
