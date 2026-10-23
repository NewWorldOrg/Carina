using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

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
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains("187", StringComparison.Ordinal));
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

    private static string RemadePathOf(EncodeHarness harness, EncodeJob job)
        => Path.Combine(harness.WorkDirectory, job.RemadeFileName.Value);
}
