using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// Makes a reduced copy of a broadcast synthesised with the ffmpeg the application runs, in the shape the
/// copies were made in, imports it, and holds what was kept against what following the same broadcast
/// keeps: the quiet and the dark at the same moments, fingerprints differing in fewer than a tenth of their
/// bits, frames as bright, and outlines of the corners alike. The copy's sound is encoded at a rate this
/// ffmpeg's own Opus encoder carries without loss worth the name, so what is measured is the import.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class ReducedCopyImportMaterialTests : IDisposable
{
    private const double Tolerance = 0.05;

    private const byte Quiet = 120;

    private const byte Dark = 30;

    private const double PictureFrame = 1001.0 / 30_000;

    private const double SoundFrame = 1024.0 / 48_000;

    private const double BreakLasts = 0.4;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly double[] Breaks = [4, 9];

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string room = Directory.CreateTempSubdirectory("carina-import-").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "what is imported from a reduced copy of a broadcast is close to what following the broadcast keeps, and the copy is left as it was")]
    public async Task WhatIsImportedIsCloseToWhatFollowingKeeps()
    {
        string source = await WriteAsync(
            "murmur",
            new SyntheticBroadcast
            {
                Murmuring = true,
                WithCaptions = false,
                WithSuperimpose = false,
                Length = TimeSpan.FromSeconds(20),
                PictureLateBy = TimeSpan.FromSeconds(0.3),
                QuietBreaks = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(13)],
            });
        Taken followed = await FollowAsync(source);
        ReducedCopy copy = await CopyAsync(source, "copy");
        IReadOnlyList<(string File, string Hash)> before = Hashed(copy.Directory);

        Taken imported = await ImportAsync(copy);

        Assert.Equal((LearningExtractionState.Done, ExtractionVersion.CurrentFromReducedCopy), (imported.Record.State, imported.Record.Version));
        Assert.InRange(imported.Record.ReadThrough.TotalSeconds, 19.5, 20.5);
        Assert.NotNull(imported.Record.Sound);
        Assert.Empty(imported.Record.Gaps);
        Assert.Equal(
            followed.Blocks.Select(block => (block.Kind, block.Chunk)),
            imported.Blocks.Select(block => (block.Kind, block.Chunk)));
        AssertSameMoments(followed.QuietMiddles(), imported.QuietMiddles(), "quiet");
        AssertSameMoments(followed.DarkMiddles(), imported.DarkMiddles(), "dark");
        Assert.True(Disagreeing(followed, imported) < 0.1, FormattableString.Invariant($"{Disagreeing(followed, imported):0.000} of the fingerprint bits disagree"));
        Assert.True(BrightnessApart(followed, imported) < 2, FormattableString.Invariant($"the frames are {BrightnessApart(followed, imported):0.00} steps of grey apart"));
        Assert.True(OutlinesApart(followed, imported) < 0.1, FormattableString.Invariant($"{OutlinesApart(followed, imported):0.000} of the corners' outline bits disagree"));
        Assert.Equal(before, Hashed(copy.Directory));
    }

    [Fact(DisplayName = "a reduced copy of a programme that begins after another stream of its file is put on the recording's own time, the moments made quiet and dark where they are")]
    public async Task ACopyIsPutOnTheRecordingsOwnTime()
    {
        string source = Path.Combine(room, "later" + SyntheticBroadcast.TransportStream);
        await FfmpegProgramme.RunAsync(FfmpegProgramme.Default, LaterProgramme(source), Cancel);
        using JsonDocument probed = JsonDocument.Parse(await ProbeAsync(source));
        double fileBegins = Begins(probed.RootElement.GetProperty("format"));
        double pictureBegins = Begins(probed.RootElement, "video");
        double soundBegins = Begins(probed.RootElement, "audio");
        ReducedCopy copy = await CopyAsync(source, "later-copy");

        Taken imported = await ImportAsync(copy);

        Assert.True(Math.Min(pictureBegins, soundBegins) - fileBegins > 1.5, "the programme does not begin after the other stream");
        Assert.Equal(Math.Min(pictureBegins, soundBegins) - fileBegins, copy.StartsAt.TotalSeconds, 0.000_001);
        Assert.Equal(LearningExtractionState.Done, imported.Record.State);
        LearningDataGap head = Assert.Single(imported.Record.Gaps);
        Assert.Equal(TimeSpan.Zero, head.From);
        Assert.Equal(soundBegins - fileBegins, head.Until.TotalSeconds, 0.01);
        AssertAt(
            [.. imported.QuietMiddles().Where(middle => middle >= head.Until.TotalSeconds)],
            [.. Breaks.Select(at => soundBegins - fileBegins + Middle(at, SoundFrame))],
            "quiet");
        AssertAt(imported.DarkMiddles(), [.. Breaks.Select(at => pictureBegins - fileBegins + Middle(at, PictureFrame))], "dark");
    }

    [Fact(DisplayName = "a reduced copy carrying captions gives whether captions are shown in each second, placed as the recording's own captions are")]
    public async Task ACopyCarryingCaptionsGivesWhetherTheyAreShown()
    {
        string source = await WriteAsync(
            "captioned",
            new SyntheticBroadcast { Murmuring = true, WithCaptions = false, WithSuperimpose = false, Length = TimeSpan.FromSeconds(6) });
        ReducedCopy copy = await CopyAsync(source, "captioned-copy");
        using JsonDocument probed = JsonDocument.Parse(await ProbeAsync(source));
        TimeSpan starts = TimeSpan.FromSeconds(Begins(probed.RootElement.GetProperty("format")));
        CaptionRecord captions = new(
            1440,
            1080,
            starts,
            [
                new CaptionCue((long)((starts.TotalSeconds + 2.5) * CaptionCue.Hertz), new CaptionPlacement(10, 10, 100, 40, new byte[] { 1, 2, 3 })),
                new CaptionCue((long)((starts.TotalSeconds + 4.25) * CaptionCue.Hertz), null),
            ]);
        await File.WriteAllBytesAsync(copy.PathOf(ReducedCopy.CaptionsFile), CaptionRecordFormat.Written(captions));

        Taken imported = await ImportAsync(copy with { Captions = copy.PathOf(ReducedCopy.CaptionsFile) });

        LearningDataBlock shown = Assert.Single(imported.Blocks, block => block.Kind is LearningDataKind.CaptionPresence);
        Assert.True(shown.TryRead(out LearningDataPart? part));
        Assert.Equal([0, 0, 1, 1, 1, 0], part.Captions[..6].ToArray());
        Assert.DoesNotContain(CaptionPresence.Shown, part.Captions[6..].ToArray());
        Assert.Equal(ExtractionVersion.CurrentFromReducedCopy, shown.Version);
        Assert.Equal(LearningExtractionState.Done, imported.Record.State);
    }

    [Fact(DisplayName = "a reduced copy whose tiles of the corners are not the shape of a copy fails, saying so, and keeps no outline")]
    public async Task ACopyOfAnotherShapeFails()
    {
        string source = await WriteAsync(
            "misshapen",
            new SyntheticBroadcast { Murmuring = true, WithCaptions = false, WithSuperimpose = false, Length = TimeSpan.FromSeconds(4) });
        ReducedCopy copy = await CopyAsync(source, "misshapen-copy", tileWidth: 200);

        Taken imported = await ImportAsync(copy);

        Assert.Equal(LearningExtractionState.Failed, imported.Record.State);
        Assert.Contains("480 by 270", imported.Record.Failure?.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(imported.Blocks, block => block.Kind is LearningDataKind.CornerOutlines);
    }

    private static void AssertSameMoments(List<double> followed, List<double> imported, string what)
    {
        Assert.Equal(2, followed.Count);
        Assert.Equal(followed.Count, imported.Count);

        for (int which = 0; which < followed.Count; which++)
        {
            Assert.True(
                Math.Abs(followed[which] - imported[which]) <= Tolerance,
                FormattableString.Invariant($"the {what} followed at {followed[which]:0.000}s was imported at {imported[which]:0.000}s"));
        }
    }

    private static IReadOnlyList<string> LaterProgramme(string destination)
    {
        string whenever = string.Join('+', Breaks.Select(at => FormattableString.Invariant($"between(t\\,{at}\\,{at + BreakLasts})")));

        return
        [
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-itsoffset", "2", "-f", "lavfi", "-i", FormattableString.Invariant($"testsrc2=size=720x480:rate=30000/1001,drawbox=x=0:y=0:w=iw:h=ih:color=black@1:t=fill:enable={whenever}"),
            "-itsoffset", "2", "-f", "lavfi", "-i", FormattableString.Invariant($"anoisesrc=color=pink:seed=7:amplitude=0.4:sample_rate=48000,volume=0:enable={whenever}"),
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000",
            "-t", "14", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg2video", "-b:v", "4M", "-c:a", "aac", "-ac", "2",
            "-program", FormattableString.Invariant($"program_num={SyntheticBroadcast.SomeProgramNumber}:st=0:st=1"),
            "-program", FormattableString.Invariant($"program_num={SyntheticBroadcast.SomeProgramNumber + 1}:st=2"),
            "-f", "mpegts", "-mpegts_m2ts_mode", "0", destination,
        ];
    }

    private static double Begins(JsonElement format) => double.Parse(format.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);

    private static double Begins(JsonElement probe, string kind)
        => Begins(probe.GetProperty("programs").EnumerateArray()
            .Single(programme => programme.GetProperty("program_id").GetInt32() == SyntheticBroadcast.SomeProgramNumber)
            .GetProperty("streams").EnumerateArray()
            .First(stream => stream.GetProperty("codec_type").GetString() == kind));

    private static double Middle(double from, double frame)
    {
        double first = Math.Ceiling((from / frame) - 1e-9) * frame;
        double end = (Math.Floor(((from + BreakLasts) / frame) + 1e-9) + 1) * frame;

        return (first + end) / 2;
    }

    private static void AssertAt(List<double> found, IReadOnlyList<double> expected, string what)
    {
        Assert.Equal(expected.Count, found.Count);

        for (int which = 0; which < expected.Count; which++)
        {
            Assert.True(
                Math.Abs(found[which] - expected[which]) <= Tolerance,
                FormattableString.Invariant($"the {what} at {expected[which]:0.000}s was found at {found[which]:0.000}s"));
        }
    }

    private static double Disagreeing(Taken followed, Taken imported)
    {
        int count = Math.Min(followed.Fingerprints.Count, imported.Fingerprints.Count);
        long disagreeing = Enumerable.Range(0, count).Sum(at => (long)SoundFingerprint.Disagreeing(followed.Fingerprints[at], imported.Fingerprints[at]));

        return disagreeing / (double)(count * SoundFingerprint.Bits);
    }

    private static double BrightnessApart(Taken followed, Taken imported)
    {
        List<double> apart = [];

        foreach ((double at, byte brightness) in followed.Frames)
        {
            (double At, byte Brightness) nearest = imported.Frames.MinBy(frame => Math.Abs(frame.At - at));

            if (Math.Abs(nearest.At - at) < PictureFrame / 2)
            {
                apart.Add(Math.Abs(nearest.Brightness - brightness));
            }
        }

        Assert.True(apart.Count > followed.Frames.Count * 9 / 10, "too few frames fell together");

        return apart.Average();
    }

    private static double OutlinesApart(Taken followed, Taken imported)
    {
        int pictures = Math.Min(followed.Outlines.Count, imported.Outlines.Count);
        long disagreeing = 0;
        long on = 0;

        for (int picture = 0; picture < pictures; picture++)
        {
            for (int at = 0; at < CornerOutline.Bytes; at++)
            {
                disagreeing += BitOperations.PopCount((uint)(followed.Outlines[picture][at] ^ imported.Outlines[picture][at]));
                on += BitOperations.PopCount(followed.Outlines[picture][at]);
            }
        }

        Assert.True(on > 0, "the corners hold no outline to compare");

        return disagreeing / (double)(pictures * CornerOutline.Bits);
    }

    private static IReadOnlyList<(string File, string Hash)> Hashed(string directory)
        => [.. Directory.EnumerateFiles(directory)
            .Order(StringComparer.Ordinal)
            .Select(file => (Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))))];

    private async Task<string> WriteAsync(string name, SyntheticBroadcast broadcast)
        => await broadcast.WriteAsync(Path.Combine(room, name + SyntheticBroadcast.TransportStream));

    /// <summary>
    /// A reduced copy of the broadcast, made the way the copies were made: one ffmpeg reading the broadcast once,
    /// the frames 64 by 36 in grey at every frame, the four corners of the picture shrunk to 960 by 540 cut out
    /// 240 by 135 and laid out two by two four times a second, and the sound as the half sum and the half
    /// difference of its channels at 16 kHz, each alone in its file.
    /// </summary>
    private async Task<ReducedCopy> CopyAsync(string source, string name, int tileWidth = CornerTiles.TileWidth)
    {
        string directory = Path.Combine(room, name);
        int program = SyntheticBroadcast.SomeProgramNumber;
        string graph = FormattableString.Invariant($"[0:p:{program}:v:0]split=2[a][b];[a]scale=64:36:flags=area,format=gray,setsar=1[f];")
            + "[b]fps=4,scale=960:540:flags=area,format=gray,setsar=1,split=4[q1][q2][q3][q4];"
            + FormattableString.Invariant($"[q1]crop={tileWidth}:135:0:0[tl];[q2]crop={tileWidth}:135:720:0[tr];")
            + FormattableString.Invariant($"[q3]crop={tileWidth}:135:0:405[bl];[q4]crop={tileWidth}:135:720:405[br];")
            + "[tl][tr][bl][br]xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0[c]";
        string[] halves = ["0.5*c0+0.5*c1", "0.5*c0-0.5*c1"];

        Directory.CreateDirectory(directory);

        await FfmpegProgramme.RunAsync(
            FfmpegProgramme.Default,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", source, "-filter_complex", graph,
                "-map", "[c]", "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "30", "-g", "40", "-pix_fmt", "yuv420p", Path.Combine(directory, ReducedCopy.Corners),
                "-map", "[f]", "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-g", "300", "-pix_fmt", "yuv420p", Path.Combine(directory, ReducedCopy.Frames),
                .. Half(program, halves[0], Path.Combine(directory, ReducedCopy.Sum)),
                .. Half(program, halves[1], Path.Combine(directory, ReducedCopy.Difference)),
            ],
            Cancel);

        ReducedCopies.Describe(room, name, new CopyDescription { Service = program }, ReducedCopy.Probe);
        await File.WriteAllTextAsync(Path.Combine(directory, ReducedCopy.Probe), await ProbeAsync(source));

        return Assert.IsType<ReducedCopy>(ReducedCopyReader.Read(directory).Copy);
    }

    private static string[] Half(int program, string pan, string destination)
        =>
        [
            "-map", string.Create(CultureInfo.InvariantCulture, $"0:p:{program}:a:0"), "-vn",
            "-af", $"aresample=async=1000,aformat=channel_layouts=stereo,pan=mono|c0={pan},aresample=16000,aresample=48000",
            "-c:a", "opus", "-strict", "-2", "-b:a", "96k", destination,
        ];

    private static async Task<Taken> FollowAsync(string source)
    {
        (LearningRecords records, HeldLearningExtractions extractions, HeldLearningData data, ServiceProvider provider) = Held();
        await using ServiceProvider disposed = provider;
        Recording recording = Made(Mounted, 7801, Now);
        FollowedRecording followed = FollowedRecording.Recorded(recording.Id, source, Service, ExtractionVersion.Current);
        LearningExtraction waiting = LearningExtraction.Waiting(recording.Id, ProgrammeCopy.Of(recording, null), Now);
        waiting.Read(ExtractionVersion.Current, Now);
        await extractions.AddAsync(waiting, Cancel);
        FfmpegLearningFollower follower = new(
            new MachineSettings(),
            new LearningFollowSettings { WhileCaughtUp = Patience },
            records,
            TimeProvider.System,
            NullLogger<FfmpegLearningFollower>.Instance);
        using CancellationTokenSource late = new(Patience);

        await follower.FollowAsync(followed, late.Token);

        return Taken.Of(extractions.Row(recording.Id)!, data.Of(recording.Id));
    }

    private static async Task<Taken> ImportAsync(ReducedCopy copy)
    {
        (LearningRecords records, HeldLearningExtractions extractions, HeldLearningData data, ServiceProvider provider) = Held();
        await using ServiceProvider disposed = provider;
        LearningExtraction waiting = LearningExtraction.Waiting(copy.Id, copy.Programme, Now);
        waiting.Import(ExtractionVersion.CurrentFromReducedCopy, Now);
        await extractions.AddAsync(waiting, Cancel);
        FfmpegReducedCopyImporter importer = new(
            new MachineSettings(),
            records,
            TimeProvider.System,
            NullLogger<FfmpegReducedCopyImporter>.Instance);
        using CancellationTokenSource late = new(Patience);

        await importer.ImportAsync(copy, late.Token);

        return Taken.Of(extractions.Row(copy.Id)!, data.Of(copy.Id));
    }

    private static (LearningRecords, HeldLearningExtractions, HeldLearningData, ServiceProvider) Held()
    {
        HeldLearningExtractions extractions = new();
        HeldLearningData data = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(extractions)
            .AddSingleton<ILearningDataRepository>(data)
            .BuildServiceProvider();

        return (new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>()), extractions, data, provider);
    }

    /// <summary>
    /// What ffprobe reads of the file the way the copies were described: the format, the programmes and the
    /// streams, read as far into the file as the copies read.
    /// </summary>
    private static async Task<string> ProbeAsync(string source)
    {
        ProcessStartInfo start = new("ffprobe") { RedirectStandardOutput = true, UseShellExecute = false };

        foreach (string argument in new[] { "-v", "quiet", "-analyzeduration", "20M", "-probesize", "100M", "-of", "json", "-show_format", "-show_programs", "-show_streams", source })
        {
            start.ArgumentList.Add(argument);
        }

        using Process probing = Process.Start(start)!;
        string said = await probing.StandardOutput.ReadToEndAsync();

        await probing.WaitForExitAsync();
        Assert.Equal(0, probing.ExitCode);

        return said;
    }

    private sealed record Taken(
        LearningExtraction Record,
        IReadOnlyList<LearningDataBlock> Blocks,
        IReadOnlyList<uint> Fingerprints,
        IReadOnlyList<(double At, byte Loudness)> Loudness,
        IReadOnlyList<(double At, byte Brightness)> Frames,
        IReadOnlyList<byte[]> Outlines)
    {
        public static Taken Of(LearningExtraction record, IReadOnlyList<LearningDataBlock> blocks)
        {
            List<uint> fingerprints = [];
            List<(double, byte)> loudness = [];
            List<(double, byte)> frames = [];
            List<byte[]> outlines = [];

            foreach (LearningDataBlock block in blocks.OrderBy(block => block.Chunk))
            {
                Assert.True(block.TryRead(out LearningDataPart? part), $"the {block.Kind} of chunk {block.Chunk} could not be read back");

                fingerprints.AddRange(part.Fingerprints.ToArray());
                loudness.AddRange(part.Loudness.ToArray().Select((reading, index) => (part.Starts.TotalSeconds + (index * 0.02), reading)));
                outlines.AddRange(part.CornerOutlines.ToArray().Chunk(CornerOutline.Bytes));

                if (part is { Kind: LearningDataKind.FrameLights, Clock: { } clock })
                {
                    frames.AddRange(part.Frames.ToArray().Select((light, index) => (clock.At(index).TotalSeconds, light.Brightness)));
                }
            }

            return new Taken(record, blocks, fingerprints, [.. loudness.OrderBy(reading => reading.Item1)], [.. frames.OrderBy(frame => frame.Item1)], outlines);
        }

        public List<double> QuietMiddles()
            => [.. Runs([.. Loudness.Select(reading => (reading.At, reading.Loudness >= Quiet))], 0.02).Where(run => run.Count >= 5).Select(run => run.Middle)];

        public List<double> DarkMiddles()
            => [.. Runs([.. Frames.Select(frame => (frame.At, frame.Brightness < Dark))], PictureFrame).Where(run => run.Count >= 3).Select(run => run.Middle)];

        private static List<(int Count, double Middle)> Runs(List<(double At, bool On)> readings, double step)
        {
            List<(int, double)> runs = [];
            int start = -1;

            for (int at = 0; at <= readings.Count; at++)
            {
                bool on = at < readings.Count && readings[at].On;

                if (on && start < 0)
                {
                    start = at;
                }

                if (!on && start >= 0)
                {
                    runs.Add((at - start, (readings[start].At + readings[at - 1].At + step) / 2));
                    start = -1;
                }
            }

            return runs;
        }
    }
}
