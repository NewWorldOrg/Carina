using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;

using Xunit.Abstractions;

namespace Carina.Infrastructure.Tests.Streaming;

/// <summary>
/// Takes the captions out of synthetic broadcasts with the ffmpeg the application runs: the moment each
/// change is kept at is the moment the broadcast stamped it, across the clock coming around and across a
/// jump, and it is the moment the live drawing beside a picture gives the same change.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class CaptionTranscriptionMaterialTests(ITestOutputHelper output) : IDisposable
{
    private const long ComesAround = 1L << 33;

    private const long Second = 90_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly MachineSettings Machine = new();

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(12);

    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private readonly string room = Directory.CreateTempSubdirectory("carina-captions").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public async Task BrPd016EveryChangeIsKeptAtTheMomentTheBroadcastStampedItAndTheCanvasIsThePicturesSize()
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = Whole, Captions = SyntheticCaptions.ShownThenCleared })
            .WriteAsync(Path.Combine(room, "cleared.m2ts"), Cancel);

        CaptionTranscription taken = await Transcriber().TranscribeAsync(written, Service, Cancel);
        IReadOnlyList<long> statements = await StatementsAsync(written);

        CaptionRecord record = Assert.IsType<CaptionRecord>(taken.Record);
        Assert.Equal((1440, 1080), (record.Width, record.Height));
        Assert.Equal(2, statements.Count);
        Assert.Equal([(statements[0], false), (statements[1], true)], record.Cues.Select(cue => (cue.Pts, cue.Clears)));
        Assert.Equal(1, record.Pictures);
        Assert.InRange(record.StartsAt, TimeSpan.Zero, TimeSpan.FromTicks((long)statements[0] * TimeSpan.TicksPerSecond / (long)Second));
    }

    [Fact]
    public async Task BrPd016AServiceThatCarriesNoCaptionStreamHasNoCaptionsAndIsNotAFailure()
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { WithCaptions = false })
            .WriteAsync(Path.Combine(room, "uncaptioned.m2ts"), Cancel);

        CaptionTranscription taken = await Transcriber().TranscribeAsync(written, Service, Cancel);

        Assert.True(taken.NoCaptionStream, taken.Note);
        Assert.True(taken.DrewNothing);
        Assert.Null(taken.Fault);
    }

    [Fact]
    public async Task BrPd016CaptionsDrawnOnACanvasWhoseSizeCouldNotBeReadAreAFailureRatherThanKeptMisplaced()
    {
        string written = await (SyntheticBroadcast.Of(SyntheticPicture.None) with { Length = Whole, Captions = SyntheticCaptions.ShownThenCleared })
            .WriteAsync(Path.Combine(room, "pictureless.m2ts"), Cancel);

        CaptionTranscription taken = await Transcriber().TranscribeAsync(written, Service, Cancel);

        Assert.Equal(CaptionFault.CanvasUnread, taken.Fault);
    }

    [Fact]
    public async Task BrPd016AFileWhosePictureSizeCannotBeReadAndThatShowsNoCaptionsHasNone()
    {
        string written = await (SyntheticBroadcast.Of(SyntheticPicture.None) with { WithCaptions = false })
            .WriteAsync(Path.Combine(room, "pictureless-uncaptioned.m2ts"), Cancel);

        CaptionTranscription taken = await Transcriber().TranscribeAsync(written, Service, Cancel);

        Assert.True(taken.DrewNothing, $"{taken.Fault} {taken.Note}");
    }

    [Fact]
    public async Task BrPd017AcrossTheClockComingAroundEveryMomentKeepsGoingForwardsFromBeforeZeroToAfterIt()
    {
        TimeSpan beforeItComesAround = TimeSpan.FromTicks(ComesAround * TimeSpan.TicksPerSecond / Second) - TimeSpan.FromSeconds(5);
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = Whole, StartsAt = beforeItComesAround })
            .WriteAsync(Path.Combine(room, "around.m2ts"), Cancel);

        CaptionRecord record = Assert.IsType<CaptionRecord>((await Transcriber().TranscribeAsync(written, Service, Cancel)).Record);
        IReadOnlyList<long> statements = await StatementsAsync(written);
        long[] kept = [.. record.Cues.Select(cue => cue.Pts)];

        Assert.True(kept.Length >= 8, $"{kept.Length} change(s) kept from twelve seconds of a caption every second");
        Assert.Equal(kept.Order().ToArray(), kept);
        Assert.Contains(kept, pts => pts < 0);
        Assert.Contains(kept, pts => pts > 0);
        Assert.All(kept, pts => Assert.Contains(pts, statements));
        Assert.True(record.StartsAt < TimeSpan.Zero, $"the file read as beginning at {record.StartsAt}");
        Assert.True(record.Cues[0].At >= record.StartsAt);
    }

    [Fact]
    public async Task BrPd017AFileWhoseClockJumpsPartWayThroughKeepsTheJumpInTheMomentsItKeeps()
    {
        string first = await (SyntheticBroadcast.AsMeasured() with { Length = TimeSpan.FromSeconds(6), StartsAt = TimeSpan.FromSeconds(1000) })
            .WriteAsync(Path.Combine(room, "before.m2ts"), Cancel);
        string second = await (SyntheticBroadcast.AsMeasured() with { Length = TimeSpan.FromSeconds(6), StartsAt = TimeSpan.FromSeconds(1100) })
            .WriteAsync(Path.Combine(room, "after.m2ts"), Cancel);
        string joined = Path.Combine(room, "resumed.m2ts");
        await File.WriteAllBytesAsync(joined, [.. await File.ReadAllBytesAsync(first, Cancel), .. await File.ReadAllBytesAsync(second, Cancel)], Cancel);

        CaptionRecord record = Assert.IsType<CaptionRecord>((await Transcriber().TranscribeAsync(joined, Service, Cancel)).Record);
        IReadOnlyList<long> statements = await StatementsAsync(joined);
        long[] kept = [.. record.Cues.Select(cue => cue.Pts)];
        long widestStep = kept.Zip(kept.Skip(1)).Max(pair => pair.Second - pair.First);

        Assert.Equal(kept.Order().ToArray(), kept);
        Assert.All(kept, pts => Assert.Contains(pts, statements));
        Assert.InRange(widestStep, 90 * Second, 100 * Second);
        Assert.False(record.Cues[^1].Clears, "the caption still showing when the file ends is not cleared at a moment ffmpeg made up");
        Assert.InRange(record.StartsAt, TimeSpan.FromSeconds(1000), TimeSpan.FromSeconds(1003));
    }

    [Fact]
    public async Task BrPd016ACaptionTakenOffTheScreenByTheBroadcastChangesAtTheSameMomentDrawnAloneAsBesideThePicture()
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = Whole, Captions = SyntheticCaptions.ShownThenCleared })
            .WriteAsync(Path.Combine(room, "cleared-both.m2ts"), Cancel);

        CaptionRecord alone = Assert.IsType<CaptionRecord>((await Transcriber().TranscribeAsync(written, Service, Cancel)).Record);
        IReadOnlyList<(long Pts, bool Clears)> beside = await BesideThePictureAsync(written);

        Assert.Equal(beside, alone.Cues.Select(cue => (cue.Pts, cue.Clears)).ToArray());
    }

    [Fact]
    public async Task BrPd016ACaptionThatLeavesTheScreenOnItsOwnLeavesAtTheMomentItAskedForDrawnAloneAsBesideThePicture()
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = Whole, Captions = SyntheticCaptions.ShownForAWhile })
            .WriteAsync(Path.Combine(room, "lasting.m2ts"), Cancel);

        CaptionRecord alone = Assert.IsType<CaptionRecord>((await Transcriber().TranscribeAsync(written, Service, Cancel)).Record);
        IReadOnlyList<(long Pts, bool Clears)> beside = await BesideThePictureAsync(written);
        IReadOnlyList<long> statements = await StatementsAsync(written);

        output.WriteLine($"statement {Seconds(statements[0])}");
        output.WriteLine($"alone  {string.Join(" ", alone.Cues.Select(cue => $"{Seconds(cue.Pts)}{(cue.Clears ? "x" : "+")}"))}");
        output.WriteLine($"beside {string.Join(" ", beside.Select(cue => $"{Seconds(cue.Pts)}{(cue.Clears ? "x" : "+")}"))}");

        Assert.Equal(statements[0], alone.Cues[0].Pts);
        Assert.Equal(beside, alone.Cues.Select(cue => (cue.Pts, cue.Clears)).ToArray());
        Assert.Equal(statements[0] + (SyntheticBroadcast.CaptionLastsTenths * Second / 10), alone.Cues[1].Pts);
    }

    [Theory]
    [InlineData(SyntheticCaptions.EverySecond)]
    [InlineData(SyntheticCaptions.ShownThenCleared)]
    [InlineData(SyntheticCaptions.ShownForAWhile)]
    public async Task BrPd019TheTextChangesAtTheSameMomentsAsThePicturesAndSaysWhatTheBroadcastWrote(SyntheticCaptions captions)
    {
        string written = await (SyntheticBroadcast.AsMeasured() with { Length = Whole, Captions = captions })
            .WriteAsync(Path.Combine(room, $"text-{captions}.m2ts"), Cancel);

        CaptionRecord record = Assert.IsType<CaptionRecord>((await Transcriber().TranscribeAsync(written, Service, Cancel)).Record);
        IReadOnlyList<CaptionLine> lines = Assert.IsAssignableFrom<IReadOnlyList<CaptionLine>>(record.Lines);

        output.WriteLine($"pictures {string.Join(" ", record.Cues.Select(cue => $"{Seconds(cue.Pts)}{(cue.Clears ? "x" : "+")}"))}");
        output.WriteLine($"text     {string.Join(" ", lines.Select(line => $"{Seconds(line.Pts)}{line.Text ?? "x"}"))}");

        Assert.Equal(record.Cues.Select(cue => (cue.Pts, cue.Clears)), lines.Select(line => (line.Pts, line.Clears)));
        Assert.All(lines.Where(line => !line.Clears), line => Assert.Contains(line.Text, new[] { "合成字幕", "CARINA" }));
        Assert.Contains(lines, line => line.Text == "合成字幕");
    }

    private static string Seconds(long pts) => (pts / (double)Second).ToString("0.000", CultureInfo.InvariantCulture);

    private static FfmpegCaptionTranscriber Transcriber()
    {
        MachineSettings machine = new();

        return new FfmpegCaptionTranscriber(
            machine,
            new CaptionSettings { LongestTranscription = TimeSpan.FromMinutes(2) },
            new FfprobeStreamAttributeReader(new StreamAttributeSettings(), TimeProvider.System),
            TimeProvider.System);
    }

    private static async Task<IReadOnlyList<long>> StatementsAsync(string written)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            "ffprobe",
            ["-hide_banner", "-loglevel", "error", "-select_streams", "s:0", "-show_entries", "packet=pts", "-of", "csv=p=0", "-i", written],
            TimeSpan.FromMinutes(1),
            TimeProvider.System,
            Cancel);

        Assert.True(said.ExitCode is 0, said.Complained);

        long[] stamped = [.. said.Said
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => long.Parse(line.TrimEnd(','), CultureInfo.InvariantCulture))];
        long management = stamped.Min();

        return [.. stamped.Where(pts => (pts - management) % Second == Second / 2)];
    }

    private static async Task<IReadOnlyList<(long Pts, bool Clears)>> BesideThePictureAsync(string written)
    {
        using AnonymousPipeServerStream captions = new(PipeDirection.In, HandleInheritability.Inheritable);

        var start = new ProcessStartInfo(FfmpegProgramme.Default)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, Machine, CaptionOutlet.Drawn)
            .Concat(FfmpegLiveInvocation.Delivery())
            .Concat(FfmpegLiveInvocation.CaptionDelivery(Service, int.Parse(captions.GetClientHandleAsString(), CultureInfo.InvariantCulture))))
        {
            start.ArgumentList.Add(argument);
        }

        using Process drawing = Process.Start(start)!;

        captions.DisposeLocalCopyOfClientHandle();

        Task feeding = Task.Run(async () =>
        {
            await using FileStream source = File.OpenRead(written);
            await source.CopyToAsync(drawing.StandardInput.BaseStream);
            drawing.StandardInput.Close();
        });
        Task<string> complaint = drawing.StandardError.ReadToEndAsync();
        Task picture = drawing.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        List<(long Pts, bool Clears)> changes = [];

        CaptionFlowFault? fault = await CaptionFrames.DrawAsync(
            captions,
            new CaptionCanvas(Interlaced.Size),
            (at, shown) =>
            {
                changes.Add(((long)at.Value, shown is null));

                return true;
            },
            Cancel);

        await feeding;
        await picture;
        await drawing.WaitForExitAsync();

        Assert.True(drawing.ExitCode is 0, await complaint);
        Assert.Null(fault);

        return changes;
    }
}
