using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Runs a job through the ffmpeg the application itself runs, against a synthetic broadcast, so
/// that the placement of an artefact by way of a real encode is measured rather than believed.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class EncodeRunMaterialTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(500);

    [Fact(DisplayName = "BR-ES-001: a synthetic broadcast is encoded to an artefact as long as the source, named for the recording and the profile, and the job completes")]
    public async Task ASyntheticBroadcastIsEncodedToAnArtefactAsLongAsTheSource()
    {
        using EncodeHarness harness = OnThisMachine();
        string broadcast = await SyntheticBroadcast.AsMeasured().WriteAsync(harness.Room.Under("broadcast.m2ts"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeProfile profile = harness.Defined();
        EncodeJob job = harness.Running(recording.Id, profile.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeFileName.Artefact(recording.Id, profile.Id), job.ArtefactName);
        string artefact = harness.ArtefactPathOf(job);
        Assert.True(File.Exists(artefact));
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, Assert.Single(harness.Scratch.Files).Fate);

        SourceLengthReading source = await new FfprobeSourceLength(new MachineSettings(), TimeProvider.System).ReadAsync(harness.SourcePathOf(recording), Cancel);
        SourceLengthReading made = await new FfprobeSourceLength(new MachineSettings(), TimeProvider.System).ReadAsync(artefact, Cancel);

        Assert.True(source.Measured, source.Note);
        Assert.True(made.Measured, made.Note);
        Assert.InRange(made.Length!.Value, source.Length!.Value - Tolerance, source.Length.Value + Tolerance);
        Assert.Contains(harness.RunnerLog.Said, line => line.Contains("100% of the way through", StringComparison.Ordinal));
        Assert.Equal(["h264", "aac"], await CodecsOfAsync(artefact));
    }

    [Fact(DisplayName = "BR-ED2-012: a file that is no broadcast at all fails the job with ffmpeg's exit code and its words, without a path in them, and leaves nothing behind but the recording")]
    public async Task AFileThatIsNoBroadcastFailsTheJobWithFfmpegsWords()
    {
        using EncodeHarness harness = OnThisMachine();
        Recording recording = harness.Recorded("this is not a transport stream");
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.FfmpegExitedNonZero, job.Failure!.Failure);
        Assert.Contains("the programme exited", job.Failure.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.False(File.Exists(harness.ArtefactPathOf(job)));
        Assert.True(File.Exists(harness.SourcePathOf(recording)));
        Assert.Equal([recording.FileName.Value], Directory.EnumerateFiles(harness.Room.Root).Select(Path.GetFileName));
        Assert.Empty(harness.Shelf.Snapshot());
    }

    [Fact(DisplayName = "BR-EV-004: H.265 runs on the card when this machine has one and is refused as capability unavailable when it has not, without the profile being touched")]
    public async Task H265RunsOnTheCardOrIsRefusedAsCapabilityUnavailable()
    {
        using EncodeHarness harness = OnThisMachine();
        MachineCapabilities can = await harness.MachineReader.ReadAsync(Cancel);
        string broadcast = await SyntheticBroadcast.AsMeasured().WriteAsync(harness.Room.Under("broadcast.m2ts"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined(EncodeCodec.H265).Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        if (can.Has(Faculty.EncodeH265OnTheCard))
        {
            Assert.True(ended is EncodeJobStatus.Completed, $"{ended}: {job.Failure?.Failure} {job.Failure?.Note}");
            Assert.Equal(["hevc", "aac"], await CodecsOfAsync(harness.ArtefactPathOf(job)));
        }
        else
        {
            Assert.Equal(EncodeJobStatus.Failed, ended);
            Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        }
    }

    private static EncodeHarness OnThisMachine()
    {
        var harness = new EncodeHarness();
        harness.Programmes = new MachineSettings();
        harness.MachineReader = new MachineCapabilityReader(harness.Programmes, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(harness.Programmes, TimeProvider.System);

        return harness;
    }

    private static async Task<IReadOnlyList<string>> CodecsOfAsync(string file)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            "ffprobe",
            ["-hide_banner", "-loglevel", "error", "-of", "default=nw=1", "-show_entries", "stream=codec_name", "-i", file],
            TimeSpan.FromSeconds(30),
            TimeProvider.System,
            Cancel);

        Assert.Equal(0, said.ExitCode);

        return [.. FfprobeRecords.From(said.Said).Select(record => record.Value("codec_name")).OfType<string>()];
    }
}
