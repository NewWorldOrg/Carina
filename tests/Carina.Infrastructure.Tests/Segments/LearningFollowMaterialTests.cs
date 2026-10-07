using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// Follows broadcasts synthesised with the ffmpeg the application runs and reads back what was kept:
/// stretches made quiet and dark at known moments must be found where they are on the recording's own
/// time, through a recording carried on after a gap, a sound that turns mono and back, a picture that
/// changes size, and a file still being written while it is read.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class LearningFollowMaterialTests : IDisposable
{
    private const double Tolerance = 0.05;

    private const double BreakLasts = 0.4;

    private const double SoundFrame = 1024.0 / 48_000;

    private const double PictureFrame = 1001.0 / 30_000;

    private const byte Quiet = 120;

    private const byte Dark = 30;

    private const int ShortestBreakReadings = 5;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private readonly string room = Directory.CreateTempSubdirectory("carina-follow-").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "stretches made quiet and dark at known moments are found where they are on the recording's own time, and the record is done")]
    public async Task KnownBreaksAreFoundWhereTheyAre()
    {
        Part part = await WriteAsync("whole", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(20), QuietBreaks = [At(5), At(13)] });

        Followed followed = await FollowAsync(part.Source);

        Assert.Equal(LearningExtractionState.Done, followed.Record.State);
        Assert.Empty(followed.Record.Gaps);
        Assert.InRange(followed.Record.ReadThrough.TotalSeconds, 19.5, 20.5);
        Assert.NotNull(followed.Record.Sound);
        AssertBreaks(followed, part, [(part, 5), (part, 13)]);
    }

    [Fact(DisplayName = "a file still being written is read on as it grows, and gives what the whole file gives")]
    public async Task AFileStillBeingWrittenIsReadOnAsItGrows()
    {
        Part part = await WriteAsync("grown", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(16), QuietBreaks = [At(6)] });
        Followed whole = await FollowAsync(part.Source);
        string growing = Path.Combine(room, "growing" + SyntheticBroadcast.TransportStream);

        await File.WriteAllBytesAsync(growing, []);

        Followed grown = await FollowAsync(growing, followed => GrowAsync(part.Source, growing, followed));

        Assert.Equal(LearningExtractionState.Done, grown.Record.State);
        Assert.Equal(Written(whole), Written(grown));
        AssertBreaks(grown, part, [(part, 6)]);
    }

    [Fact(DisplayName = "a recording carried on after a gap keeps the gap as one, and what follows the gap stays at its own time")]
    public async Task ARecordingCarriedOnAfterAGapKeepsTheGap()
    {
        Part before = await WriteAsync("before", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(12), QuietBreaks = [At(5)] });
        Part after = await WriteAsync("after", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(10), StartsAt = At(30), QuietBreaks = [At(4)] });
        string joined = Joined("carried-on", before, after);

        Followed followed = await FollowAsync(joined);

        LearningDataGap gap = Assert.Single(followed.Record.Gaps);
        Assert.Equal(LearningExtractionState.Done, followed.Record.State);
        Assert.InRange(gap.From.TotalSeconds, Since(before, before, 12) - 0.1, Since(before, before, 12) + 0.1);
        Assert.InRange(gap.Until.TotalSeconds, Since(before, after, 0) - 0.1, Since(before, after, 0) + 0.1);
        AssertBreaks(followed, before, [(before, 5), (after, 4)]);
    }

    [Fact(DisplayName = "a sound that turns mono and back to stereo carries on without a break, and every stretch made quiet stays where it is")]
    public async Task ASoundThatTurnsMonoAndBackCarriesOn()
    {
        Part first = await WriteAsync("stereo", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(10), QuietBreaks = [At(4)] });
        Part second = await WriteAsync("mono", new SyntheticBroadcast { Sound = SyntheticSound.Mono, Length = TimeSpan.FromSeconds(10), StartsAt = At(10), QuietBreaks = [At(4)] });
        Part third = await WriteAsync("stereo-again", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(10), StartsAt = At(20), QuietBreaks = [At(4)] });

        Followed followed = await FollowAsync(Joined("mono-between", first, second, third));

        Assert.Equal(LearningExtractionState.Done, followed.Record.State);
        Assert.Empty(followed.Record.Gaps);
        AssertBreaks(followed, first, [(first, 4), (second, 4), (third, 4)]);
    }

    [Fact(DisplayName = "a picture that changes size keeps its frames in one shape and in step")]
    public async Task APictureThatChangesSizeKeepsItsFramesInStep()
    {
        Part first = await WriteAsync("high", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(10), QuietBreaks = [At(4)] });
        Part second = await WriteAsync("standard", new SyntheticBroadcast { Picture = SyntheticPicture.StandardDefinition, Length = TimeSpan.FromSeconds(10), StartsAt = At(10), QuietBreaks = [At(4)] });
        Part third = await WriteAsync("high-again", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(10), StartsAt = At(20), QuietBreaks = [At(4)] });

        Followed followed = await FollowAsync(Joined("sizes", first, second, third));

        Assert.Equal(LearningExtractionState.Done, followed.Record.State);
        Assert.Empty(followed.Record.Gaps);
        Assert.InRange(followed.Frames.Count, (int)(29.5 * 29.97), (int)(30.5 * 29.97));
        AssertBreaks(followed, first, [(first, 4), (second, 4), (third, 4)]);
    }

    [Fact(DisplayName = "a programme the recording does not carry fails the reading for want of a stream")]
    public async Task AProgrammeTheRecordingDoesNotCarryFailsForWantOfAStream()
    {
        Part part = await WriteAsync("other", new SyntheticBroadcast { Length = TimeSpan.FromSeconds(3) });

        Followed followed = await FollowAsync(part.Source, service: new ServiceId(SyntheticBroadcast.SomeProgramNumber + 1));

        Assert.Equal(LearningExtractionState.Failed, followed.Record.State);
        Assert.Equal(ExtractionFailure.StreamMissing, followed.Record.Failure?.Failure);
    }

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    private async Task<Part> WriteAsync(string name, SyntheticBroadcast broadcast)
    {
        string source = await broadcast.WriteAsync(Path.Combine(room, name + SyntheticBroadcast.TransportStream));
        using JsonDocument probed = JsonDocument.Parse(await RunAsync(
            "ffprobe",
            ["-v", "error", "-show_entries", "format=start_time:stream=codec_type,start_time", "-of", "json", source]));
        double video = probed.RootElement.GetProperty("streams").EnumerateArray()
            .Where(stream => stream.GetProperty("codec_type").GetString() is "video")
            .Select(stream => Seconds(stream.GetProperty("start_time")))
            .First();

        return new Part(source, Seconds(probed.RootElement.GetProperty("format").GetProperty("start_time")), video);
    }

    private string Joined(string name, params Part[] parts)
    {
        string joined = Path.Combine(room, name + SyntheticBroadcast.TransportStream);

        File.WriteAllBytes(joined, [.. parts.SelectMany(part => File.ReadAllBytes(part.Source))]);

        return joined;
    }

    private static async Task GrowAsync(string from, string into, FollowedRecording followed)
    {
        byte[] whole = await File.ReadAllBytesAsync(from);

        await using (FileStream writing = new(into, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            for (int at = 0; at < whole.Length; at += 128 * 1024)
            {
                await writing.WriteAsync(whole.AsMemory(at, Math.Min(128 * 1024, whole.Length - at)));
                await writing.FlushAsync();
                await Task.Delay(15);
            }
        }

        followed.Ended();
    }

    private static async Task<Followed> FollowAsync(
        string source,
        Func<FollowedRecording, Task>? alongside = null,
        ServiceId? service = null)
    {
        HeldLearningExtractions extractions = new();
        HeldLearningData data = new();
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(extractions)
            .AddSingleton<ILearningDataRepository>(data)
            .BuildServiceProvider();
        Recording recording = Made(Mounted, 7501, Now);
        FollowedRecording followed = new(recording.Id, source, service ?? Service, ExtractionVersion.Current);
        FfmpegLearningFollower follower = new(
            new MachineSettings(),
            new LearningFollowSettings { WhileCaughtUp = TimeSpan.FromMilliseconds(20) },
            new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>()),
            TimeProvider.System,
            NullLogger<FfmpegLearningFollower>.Instance);

        await extractions.AddAsync(LearningExtraction.Following(recording.Id, ProgrammeCopy.Of(recording, null), ExtractionVersion.Current, Now), CancellationToken.None);

        if (alongside is null)
        {
            followed.Ended();
        }

        using CancellationTokenSource late = new(Patience);
        Task growing = alongside?.Invoke(followed) ?? Task.CompletedTask;

        await follower.FollowAsync(followed, late.Token);
        await growing;

        LearningExtraction record = extractions.Row(recording.Id) ?? throw new InvalidOperationException("The record went.");

        return Followed.Of(record, data.Of(recording.Id));
    }

    private static void AssertBreaks(Followed followed, Part zero, IReadOnlyList<(Part Part, double At)> breaks)
    {
        List<double> quiet = [.. followed.QuietMiddles().Where(middle => !followed.Record.Gaps.Any(gap => middle >= gap.From.TotalSeconds && middle < gap.Until.TotalSeconds))];
        List<double> dark = followed.DarkMiddles();

        Assert.Equal(breaks.Count, quiet.Count);
        Assert.Equal(breaks.Count, dark.Count);

        for (int which = 0; which < breaks.Count; which++)
        {
            (Part part, double at) = breaks[which];
            double heard = Since(zero, part, Middle(at, SoundFrame));
            double seen = Since(zero, part, Middle(at, PictureFrame));

            Assert.True(Math.Abs(quiet[which] - heard) <= Tolerance, FormattableString.Invariant($"the quiet at {at}s of its part was found at {quiet[which]:0.000}s, not {heard:0.000}s"));
            Assert.True(Math.Abs(dark[which] - seen) <= Tolerance, FormattableString.Invariant($"the dark at {at}s of its part was found at {dark[which]:0.000}s, not {seen:0.000}s"));
        }
    }

    private static double Middle(double from, double frame)
    {
        double first = Math.Ceiling((from / frame) - 1e-9) * frame;
        double end = (Math.Floor(((from + BreakLasts) / frame) + 1e-9) + 1) * frame;

        return (first + end) / 2;
    }

    private static double Since(Part zero, Part part, double broadcastSeconds) => part.VideoStart + broadcastSeconds - zero.FormatStart;

    private static List<(LearningDataKind, int, string)> Written(Followed followed)
        => [.. followed.Blocks.Select(block => (block.Kind, block.Chunk, Convert.ToHexString(block.Bytes)))];

    private static double Seconds(JsonElement element) => double.Parse(element.GetString()!, CultureInfo.InvariantCulture);

    private static async Task<string> RunAsync(string programme, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new(programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process running = Process.Start(start)!;
        Task<string> said = running.StandardOutput.ReadToEndAsync();
        Task<string> complaint = running.StandardError.ReadToEndAsync();

        await running.WaitForExitAsync();

        Assert.True(running.ExitCode is 0, $"{programme} exited {running.ExitCode}: {await complaint}");

        return await said;
    }

    private sealed record Part(string Source, double FormatStart, double VideoStart);

    private sealed record Followed(
        LearningExtraction Record,
        IReadOnlyList<LearningDataBlock> Blocks,
        IReadOnlyList<(double At, byte Loudness)> Loudness,
        IReadOnlyList<(double At, byte Brightness)> Frames)
    {
        public static Followed Of(LearningExtraction record, IReadOnlyList<LearningDataBlock> blocks)
        {
            List<(double, byte)> loudness = [];
            List<(double, byte)> frames = [];

            foreach (LearningDataBlock block in blocks)
            {
                Assert.True(block.TryRead(out LearningDataPart? part), $"the {block.Kind} of chunk {block.Chunk} could not be read back");

                if (part.Kind is LearningDataKind.Loudness)
                {
                    loudness.AddRange(part.Loudness.ToArray().Select((reading, index) => (part.Starts.TotalSeconds + (index * 0.02), reading)));
                }

                if (part is { Kind: LearningDataKind.FrameLights, Clock: { } clock })
                {
                    frames.AddRange(part.Frames.ToArray().Select((light, index) => (clock.At(index).TotalSeconds, light.Brightness)));
                }
            }

            return new Followed(record, blocks, [.. loudness.OrderBy(reading => reading.Item1)], [.. frames.OrderBy(frame => frame.Item1)]);
        }

        public List<double> QuietMiddles()
            => [.. Runs(Loudness.Select(reading => (reading.At, reading.Loudness >= Quiet)).ToList(), 0.02).Where(run => run.Count >= ShortestBreakReadings).Select(run => run.Middle)];

        public List<double> DarkMiddles()
            => [.. Runs(Frames.Select(frame => (frame.At, frame.Brightness < Dark)).ToList(), PictureFrame).Where(run => run.Count >= 3).Select(run => run.Middle)];

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
