using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Machines;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// The recording the harness writes is "the broadcast", thirteen bytes. The stand-ins write a picture larger or
/// smaller than that depending on the rate they are handed: the harness's profile is rate factor 22 and
/// quantiser 24, so the run that makes it again is handed 26 on the processor and 28 on the card.
/// </summary>
public sealed class EncodeJobRunnerRemakeTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const string Larger = "a picture larger than its recording";

    private const string StillLarger = "a smaller picture, still large";

    private const string Small = "small";

    private static string Writes(string first, string second, string coarser = "-crf 26")
        => $"""
            case " $* " in
              *" {coarser} "*) printf '{second}' > "$destination" ;;
              *) printf '{first}' > "$destination" ;;
            esac
            printf 'out_time_us=10000000\nspeed=2.0x\nprogress=end\n'
            """;

    private static string Counted(string script, string runs)
        => $"printf 'run\\n' >> \"{runs}\"\n{script}";

    [Fact(DisplayName = "an artefact larger than its recording is made again four coarser, the smaller second is placed, and the job says it made it again because the first was larger")]
    public async Task AnArtefactLargerThanItsRecordingIsMadeAgainCoarser()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(Larger, Small), runs));
        List<EncodeRemake?> saved = [];
        harness.Jobs.WhenSaving = job => saved.Add(job.Remade);
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(2, File.ReadAllLines(runs).Length);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Contains(EncodeRemake.LargerThanTheSource, saved);
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.SourcePathOf(recording)));
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(
            [$"recorded {job.WorkFileName.Value}", $"recorded {job.RemadeFileName.Value}"],
            harness.Scratch.Moves.Where(move => move.StartsWith("recorded", StringComparison.Ordinal)));
        Assert.Equal(EncodeScratchFate.Removed, harness.Scratch.Files.Single(file => file.FileName.Equals(job.WorkFileName)).Fate);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.RemadeFileName)).Fate);
        RemovedBeforeThePlacing(harness, unused: job.WorkFileName, placed: job.RemadeFileName);
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains("larger than its recording", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a second try still larger than the recording but smaller than the first is placed, and the job says it kept something still larger")]
    public async Task ASecondTryStillLargerButSmallerThanTheFirstIsPlaced()
    {
        using var harness = new EncodeHarness();
        harness.Standing(Writes(Larger, StillLarger));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(StillLarger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.StillLargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "a second try no smaller than the first leaves the first to be placed and is removed, and the job says the second came out no smaller")]
    public async Task ASecondTryNoSmallerThanTheFirstLeavesTheFirst()
    {
        using var harness = new EncodeHarness();
        harness.Standing(Writes(StillLarger, Larger));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(StillLarger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.WorkFileName)).Fate);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(EncodeScratchFate.Removed, harness.Scratch.Files.Single(file => file.FileName.Equals(job.RemadeFileName)).Fate);
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
    }

    [Fact(DisplayName = "a second try that fails leaves the first to be placed, the job completes, and it says the second came out no smaller")]
    public async Task ASecondTryThatFailsLeavesTheFirst()
    {
        using var harness = new EncodeHarness();
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*) printf 'half a picture' > "$destination"; exit 187 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Null(job.Failure);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
        Assert.Contains(
            harness.RunnerLog.Warnings,
            line => line.Contains("187", StringComparison.Ordinal) && line.Contains(nameof(EncodeFailure.FfmpegExitedNonZero), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a second try that runs out of room is told apart in the log from one the programme refused, the job completes on the first, and what the second left is removed at once")]
    public async Task ASecondTryThatRunsOutOfRoomIsToldApart()
    {
        using var harness = new EncodeHarness();
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*) printf 'half a picture' > "$destination"; echo "av_interleaved_write_frame(): No space left on device" >&2; exit 1 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains(nameof(EncodeFailure.NotEnoughRoom), StringComparison.Ordinal));
        Assert.DoesNotContain(harness.RunnerLog.Warnings, line => line.Contains(nameof(EncodeFailure.FfmpegExitedNonZero), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a recording whose captions were taken has the text track put into the second it kept, the first is gone before the captions are made, and what is placed is the copy with the track")]
    public async Task TheTextTrackGoesIntoTheSecondItKept()
    {
        using var harness = new EncodeHarness();
        string muxed = harness.Room.Under("muxed");
        string withTheTrack = harness.Room.Under("with-the-track.mp4");
        File.WriteAllBytes(withTheTrack, Mp4WithASubtitleTrack());
        harness.Standing($"""
            case "$destination" in
              *{EncodeFileName.CaptionedExtension})
                taken=""
                previous=""
                for argument in "$@"; do
                  if [ "$previous" = "-i" ] && [ -z "$taken" ]; then taken=$argument; fi
                  previous=$argument
                done
                printf '%s' "$taken" > "{muxed}"
                cp "{withTheTrack}" "$destination" ;;
              *)
                {Writes(Larger, Small)} ;;
            esac
            """);
        harness.Programmes = harness.Programmes with { Prober = Prober(harness) };
        Recording recording = harness.Recorded();
        CaptionRecord record = new(
            1440,
            1080,
            MeasuredHeads.Start,
            [new CaptionCue(TwoSecondsIn, null)],
            [new CaptionLine(TwoSecondsIn, "合成字幕")]);
        await harness.CaptionShelf.KeepAsync(recording.Id, record, Cancel);
        recording.Caption(CaptionState.Ready, 1, EncodeHarness.Queued.AddMinutes(20));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Equal(EncodeCaptionTrack.Added, job.CaptionTrack);
        Assert.Equal(RemadePathOf(harness, job), File.ReadAllText(muxed));
        Assert.Equal(job.CaptionedFileName, job.FileToPlace);
        Assert.Equal(File.ReadAllBytes(withTheTrack).Length, File.ReadAllBytes(harness.ArtefactPathOf(job)).Length);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.CaptionedFileName)).Fate);

        List<string> moves = harness.Scratch.Moves;
        int firstRemoved = moves.IndexOf($"settled {job.WorkFileName.Value} Removed");
        int captionsRecorded = moves.IndexOf($"recorded {job.CaptionTrackFileName.Value}");

        Assert.True(firstRemoved >= 0 && captionsRecorded > firstRemoved, string.Join('\n', moves));
        Assert.DoesNotContain(Directory.EnumerateFiles(harness.WorkDirectory), file => !file.EndsWith(EncodeFileName.ArtefactExtension, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a stop asked for during the second try leaves the job running with what it was making again written down and both work files where they are, and starting over forgets it and writes new ones")]
    public async Task AStopDuringTheSecondTryLeavesTheJobRunningAndStartingOverForgetsIt()
    {
        using var harness = new EncodeHarness();
        string began = harness.Room.Under("began");
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*)
                if [ ! -e "{began}" ]; then
                  printf 'half a picture' > "$destination"
                  printf '%s' "$$" > "{began}"
                  exec sleep 30
                fi
                printf '{Small}' > "$destination" ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        EncodeFileName firstWork = job.WorkFileName;
        EncodeFileName firstRemade = job.RemadeFileName;
        using var stopping = new CancellationTokenSource();

        Task<EncodeJobStatus> running = harness.Runner.RunAsync(job, stopping.Token);
        await Eventually.Happens(() => File.Exists(began), "the second try began");
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(EncodeJobStatus.Running, job.Status);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Equal(2, harness.Scratch.Files.Count);
        Assert.All(harness.Scratch.Files, file => Assert.True(file.IsOwedARemoval, $"{file.FileName.Value} is left for the next start"));
        Assert.True(File.Exists(Path.Combine(harness.WorkDirectory, firstWork.Value)));
        Assert.True(File.Exists(Path.Combine(harness.WorkDirectory, firstRemade.Value)));

        job.Requeue(EncodeHarness.Started.AddMinutes(5));

        Assert.Null(job.Remade);
        Assert.NotEqual(firstWork, job.WorkFileName);
        Assert.NotEqual(firstRemade, job.RemadeFileName);

        job.Start(EncodeHarness.Started.AddMinutes(6));
        EncodeJobStatus again = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, again);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Contains($"recorded {job.WorkFileName.Value}", harness.Scratch.Moves);
        Assert.Contains($"recorded {job.RemadeFileName.Value}", harness.Scratch.Moves);
        Assert.DoesNotContain(Directory.EnumerateFiles(harness.WorkDirectory), file => file.EndsWith(EncodeFileName.WorkExtension, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a job called off while it makes its artefact again has both its work files swept, and its run is dropped for the ledger's word")]
    public async Task AJobCalledOffWhileItMakesItAgainHasBothWorkFilesSwept()
    {
        using var harness = new EncodeHarness();
        harness.Jobs.KeepsItsOwnRows = true;
        string began = harness.Room.Under("began");
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*)
                printf 'half a picture' > "$destination"
                printf '%s' "$$" > "{began}"
                exec sleep 30 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        Recording recording = harness.Recorded();
        harness.Jobs.Jobs.Add(EncodeJob.Queue(
            EncodeJobId.New(),
            recording.Id,
            harness.Defined().Id,
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeHarness.Queued));
        EncodeJob job = (await harness.Jobs.ClaimNextAsync(EncodeHarness.Started, Cancel)).Job!;
        EncodeJob? calledOff = null;
        harness.Jobs.WhenSaving = saved =>
        {
            if (calledOff is not null && !ReferenceEquals(saved, calledOff))
            {
                throw new EncodeJobMovedMeanwhileException(saved.Id);
            }
        };

        Task<EncodeJobStatus> running = harness.Runner.RunAsync(job, Cancel);
        await Eventually.Happens(
            () => File.Exists(began)
                && harness.Jobs.Jobs.Single().Programme?.ProcessId == int.Parse(File.ReadAllText(began), CultureInfo.InvariantCulture),
            "the second try began and the ledger holds its programme");

        EncodeJob ledger = (await harness.Jobs.FindAsync(job.Id, Cancel))!;
        RunningProgramme second = ledger.Programme!;
        calledOff = ledger;
        ledger.Cancel(harness.Clock.GetUtcNow().UtcDateTime);
        await harness.Jobs.SaveAsync(ledger, Cancel);
        StrayFate stopped = new StrayProgrammes().Stop(second);
        await harness.Cleaner.ClearAsync(ledger, Cancel);

        await Assert.ThrowsAsync<EncodeJobMovedMeanwhileException>(() => running);
        Assert.Equal(StrayFate.Stopped, stopped);
        Assert.Equal(EncodeJobStatus.Cancelled, harness.Jobs.Jobs.Single().Status);
        Assert.False(File.Exists(Path.Combine(harness.WorkDirectory, job.WorkFileName.Value)));
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(2, harness.Scratch.Files.Count);
        Assert.All(harness.Scratch.Files, file => Assert.Equal(EncodeScratchFate.Removed, file.Fate));
    }

    [Fact(DisplayName = "on the card the second try is handed the quantiser four coarser")]
    public async Task OnTheCardTheSecondTryIsHandedTheQuantiserFourCoarser()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { Prefer = EncodeEncoder.Vaapi };
        harness.Machine.Can = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard],
            string.Empty);
        harness.Standing(Writes(Larger, Small, coarser: "-qp 28"));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "an artefact no larger than its recording is made once")]
    public async Task AnArtefactNoLargerThanItsRecordingIsMadeOnce()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(EncodeHarness.Broadcast, Small), runs));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
        Assert.DoesNotContain(harness.Scratch.Files, file => file.FileName.Equals(job.RemadeFileName));
    }

    [Fact(DisplayName = "a recording whose file is gone by the time the encode ends is not measured against, and the artefact is placed as it was made")]
    public async Task ARecordingWhoseFileIsGoneIsNotMeasuredAgainst()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        Recording recording = harness.Recorded();
        harness.Standing(Counted($"rm -f \"{harness.SourcePathOf(recording)}\"\n{Writes(Larger, Small)}", runs));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
    }

    [Fact(DisplayName = "a profile already at the coarsest is not made again, since a second try could come out no smaller")]
    public async Task AProfileAlreadyAtTheCoarsestIsNotMadeAgain()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(Larger, Small), runs));
        EncodeProfile coarsest = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Coarsest"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryField,
            new ConstantRateFactor(ConstantRateFactor.Coarsest),
            new ConstantQuantiser(ConstantQuantiser.Coarsest),
            EncodeHarness.Queued);
        harness.Profiles.Profiles.Add(coarsest);
        EncodeJob job = harness.Running(harness.Recorded().Id, coarsest.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
    }

    private static readonly long TwoSecondsIn = (long)((MeasuredHeads.Start.TotalSeconds + 2) * 90_000);

    private const string CarriedWithout =
        """{"streams":[{"codec_name":"h264","codec_type":"video","disposition":{"default":1}},{"codec_name":"aac","codec_type":"audio","disposition":{"default":1}}],"format":{"duration":"10.000000"}}""";

    private const string CarriedWithTheTrack =
        """{"streams":[{"codec_name":"h264","codec_type":"video","disposition":{"default":1}},{"codec_name":"aac","codec_type":"audio","disposition":{"default":1}},{"codec_name":"mov_text","codec_type":"subtitle","disposition":{"default":0},"tags":{"language":"jpn"}}],"format":{"duration":"10.000000"}}""";

    private static string RemadePathOf(EncodeHarness harness, EncodeJob job)
        => Path.Combine(harness.WorkDirectory, job.RemadeFileName.Value);

    private static void RemovedBeforeThePlacing(EncodeHarness harness, EncodeFileName unused, EncodeFileName placed)
    {
        List<string> moves = harness.Scratch.Moves;
        int removed = moves.IndexOf($"settled {unused.Value} Removed");
        int became = moves.IndexOf($"settled {placed.Value} BecameTheArtefact");

        Assert.True(removed >= 0 && became > removed, string.Join('\n', moves));
    }

    /// <summary>
    /// A stand-in for ffprobe that says the file with the text track put in carries one Japanese text track off
    /// by default beside the picture and the sound, and that anything else carries the picture and the sound.
    /// </summary>
    private static string Prober(EncodeHarness harness)
        => StandInProgramme.Written(
            harness.Room.Under($"prober-{Guid.NewGuid():N}.sh"),
            $"""
            for argument in "$@"; do file=$argument; done
            case "$file" in
              *{EncodeFileName.CaptionedExtension}) printf '%s' '{CarriedWithTheTrack}' ;;
              *) printf '%s' '{CarriedWithout}' ;;
            esac
            """);

    private static byte[] Mp4WithASubtitleTrack()
        => [
            .. Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0")),
            .. Box("moov", [.. Track("vide"), .. Track("soun"), .. Track("sbtl")]),
            .. Box("mdat", new byte[64]),
        ];

    private static byte[] Track(string handler)
    {
        byte[] header = new byte[84];
        header[3] = 0x03;

        byte[] reference = new byte[24];
        Encoding.ASCII.GetBytes(handler).CopyTo(reference, 8);

        return Box("trak", [.. Box("tkhd", header), .. Box("mdia", [.. Box("mdhd", new byte[24]), .. Box("hdlr", reference)])]);
    }

    private static byte[] Box(string type, byte[] body)
    {
        byte[] box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }
}
