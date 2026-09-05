using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class SyntheticBroadcastMaterialTests : IDisposable
{
    private const string Ffprobe = "ffprobe";

    private static readonly TimeSpan PastTheProbe = TimeSpan.FromSeconds(12);

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private const string Entries =
        "stream=codec_type,codec_name,profile,channels,channel_layout:stream_tags=language:program=program_id";

    private const string Counted = "stream=codec_type,nb_read_packets";

    private readonly string room = Directory.CreateTempSubdirectory("carina-material").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public async Task TheMeasuredBroadcastReadsAsInterlacedHdStereoWithCaptionsAndData()
    {
        string written = await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "measured.m2ts"));

        StreamAttributeReading reading = await ReadAsync(written);
        IReadOnlyList<FfprobeRecord> probed = await ProbedAsync(written);

        Assert.True(reading.Measured);
        Assert.False(reading.SeveralVideoDescriptions);
        Assert.Equal(new VideoSize(1440, 1080), reading.Attributes.Size);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
        Assert.Equal(FrameRate.BroadcastFrames, reading.Attributes.Rate);
        Assert.Equal(AudioMode.Stereo, reading.Attributes.Audio);
        Assert.Equal(["mpeg2video", "aac", "arib_caption", "bin_data"], Codecs(probed));
        Assert.Equal("Profile A", probed.First(record => record.Value("codec_name") is "arib_caption").Value("profile"));
        Assert.Equal("LC", probed.First(record => record.Value("codec_name") is "aac").Value("profile"));
        Assert.Equal(["1040"], Programs(probed));
    }

    [Fact]
    public async Task TheStandardDefinitionBroadcastReadsAs480i()
    {
        StreamAttributeReading reading = await ReadAsync(
            await SyntheticBroadcast.Of(SyntheticPicture.StandardDefinition).WriteAsync(Path.Combine(room, "sd.m2ts")));

        Assert.True(reading.Measured);
        Assert.Equal(new VideoSize(720, 480), reading.Attributes.Size);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
    }

    [Fact]
    public async Task TheFullHdBroadcastReadsAs1920By1080Interlaced()
    {
        StreamAttributeReading reading = await ReadAsync(
            await SyntheticBroadcast.Of(SyntheticPicture.FullHd).WriteAsync(Path.Combine(room, "full.m2ts")));

        Assert.True(reading.Measured);
        Assert.Equal(new VideoSize(1920, 1080), reading.Attributes.Size);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
    }

    [Theory]
    [InlineData(SyntheticSound.Mono, AudioMode.Mono, "1", "mono")]
    [InlineData(SyntheticSound.Surround, AudioMode.Surround, "6", "5.1")]
    [InlineData(SyntheticSound.DualMono, AudioMode.Stereo, "2", "stereo")]
    public async Task EachSoundIsReadAsTheModeTheProbeCanTell(
        SyntheticSound sound,
        AudioMode expected,
        string channels,
        string layout)
    {
        string written = await SyntheticBroadcast.Sounding(sound).WriteAsync(Path.Combine(room, $"{sound}.m2ts"));

        StreamAttributeReading reading = await ReadAsync(written);
        FfprobeRecord probed = (await ProbedAsync(written)).First(record => record.Value("codec_type") is "audio");

        Assert.True(reading.Measured);
        Assert.Equal(expected, reading.Attributes.Audio);
        Assert.Equal(channels, probed.Value("channels"));
        Assert.Equal(layout, probed.Value("channel_layout"));
    }

    [Fact]
    public async Task TheDualMonoSoundDecodesAsTwoChannelsOfSilence()
    {
        string written = await SyntheticBroadcast.Sounding(SyntheticSound.DualMono).WriteAsync(Path.Combine(room, "dual.m2ts"));

        await FfmpegProgramme.RunAsync(
            FfmpegProgramme.Default,
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-i", written, "-map", "0:a", "-f", "null", "-"],
            CancellationToken.None);
    }

    [Fact]
    public async Task ABilingualBroadcastCarriesTwoSoundsEachNamingItsLanguage()
    {
        string written = await SyntheticBroadcast.Sounding(SyntheticSound.TwoLanguages).WriteAsync(Path.Combine(room, "bilingual.m2ts"));

        IReadOnlyList<FfprobeRecord> sounds =
            [.. (await ProbedAsync(written)).Where(record => record.Value("codec_type") is "audio")];

        Assert.Equal(
            ["jpn", "eng"],
            sounds.Select(record => record.Value("TAG:language")).OfType<string>().Distinct().ToArray());
        Assert.Equal(AudioMode.Stereo, (await ReadAsync(written)).Attributes.Audio);
    }

    [Fact]
    public async Task ABroadcastWithoutAPictureFallsBackOnEveryPictureAttributeAndMeasuresTheSound()
    {
        StreamAttributeReading reading = await ReadAsync(
            await SyntheticBroadcast.Of(SyntheticPicture.None).WriteAsync(Path.Combine(room, "sound-only.m2ts")));

        Assert.False(reading.Measured);
        Assert.Equal([StreamAttribute.Resolution, StreamAttribute.Scan, StreamAttribute.FrameRate], reading.FellBackOn);
        Assert.Equal(AudioMode.Stereo, reading.Attributes.Audio);
        Assert.Equal(StreamAttributes.SafeSide.Size, reading.Attributes.Size);
    }

    [Fact]
    public async Task TheEncodedCounterpartIsH264WithTheSoundCarriedAcross()
    {
        string original = await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "original.m2ts"));
        string encoded = Path.Combine(room, "encoded.mp4");

        await SyntheticBroadcast.EncodeAsH264Async(original, encoded);

        IReadOnlyList<FfprobeRecord> probed = await ProbedAsync(encoded);

        Assert.Equal(["h264", "aac"], Codecs(probed));
        Assert.Equal(ScanType.Progressive, (await ReadAsync(encoded)).Attributes.Scan);
    }

    [Fact]
    public async Task ARecordingWhoseLedgerRowSaysNothingAboutThePictureStillMeasuresAs480i()
    {
        DirectoryInfo mounted = Directory.CreateDirectory(Path.Combine(room, "bulk"));
        string written = await SyntheticBroadcast
            .Of(SyntheticPicture.StandardDefinition)
            .WriteAsync(Path.Combine(room, "standard-definition.m2ts"));

        Recording recording = new RecordedMaterial(mounted, new OutputRoot("bulk")).Ended(RecordingId.New(), written);
        StreamAttributeReading reading = await ReadAsync(Path.Combine(mounted.FullName, recording.FileName.Value));

        Assert.True(reading.Measured);
        Assert.Equal(new VideoSize(720, 480), reading.Attributes.Size);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
    }

    [Fact]
    public async Task TheRecordedPairIsOneProgrammeHeldTwiceAsTheTransportStreamAndAsH264()
    {
        DirectoryInfo mounted = Directory.CreateDirectory(Path.Combine(room, "pair"));
        string original = await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "pair.m2ts"));
        string encoded = Path.Combine(room, "pair.mp4");

        await SyntheticBroadcast.EncodeAsH264Async(original, encoded);

        RecordedPair pair = new RecordedMaterial(mounted, new OutputRoot("pair"))
            .Pair(RecordingId.New(), RecordingId.New(), original, encoded);

        Assert.Equal(pair.Original.EventId, pair.Encoded.EventId);
        Assert.Equal(
            ScanType.Interlaced,
            (await ReadAsync(Path.Combine(mounted.FullName, pair.Original.FileName.Value))).Attributes.Scan);
        Assert.Equal(
            ScanType.Progressive,
            (await ReadAsync(Path.Combine(mounted.FullName, pair.Encoded.FileName.Value))).Attributes.Scan);
    }

    [Fact]
    public async Task TheSameBroadcastIsWrittenByteForByteTwice()
    {
        byte[] first = await File.ReadAllBytesAsync(await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "first.m2ts")));
        byte[] second = await File.ReadAllBytesAsync(await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "second.m2ts")));

        Assert.Equal(first, second);
        Assert.InRange(first.Length, 1_000_000, 4_000_000);
    }

    [Fact]
    public async Task TheCaptionsAreDrawnByTheSameInvocationTheLiveCaptionerUses()
    {
        string written = await SyntheticBroadcast.AsMeasured().WriteAsync(Path.Combine(room, "captioned.m2ts"));
        VideoSize canvas = new(1440, 1080);

        var start = new ProcessStartInfo(FfmpegProgramme.Default)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in FfmpegCaptionInvocation.Arguments(new ServiceId(SyntheticBroadcast.SomeProgramNumber), canvas)
            .Concat(FfmpegCaptionInvocation.Delivery()))
        {
            start.ArgumentList.Add(argument);
        }

        using Process drawing = Process.Start(start)!;

        Task feeding = Task.Run(async () =>
        {
            await using FileStream source = File.OpenRead(written);
            await source.CopyToAsync(drawing.StandardInput.BaseStream);
            drawing.StandardInput.Close();
        });
        Task<string> complaint = drawing.StandardError.ReadToEndAsync();

        using var drawn = new MemoryStream();
        await drawing.StandardOutput.BaseStream.CopyToAsync(drawn);
        await feeding;
        await drawing.WaitForExitAsync();

        int frameLength = canvas.Width * canvas.Height * 4;
        int frames = (int)(drawn.Length / frameLength);
        byte[] pixels = drawn.ToArray();
        int painted = Enumerable.Range(0, frames)
            .Count(frame => pixels.AsSpan(frame * frameLength, frameLength).IndexOfAnyExcept((byte)0) >= 0);

        Assert.Equal(0, drawing.ExitCode);
        Assert.Equal(0L, drawn.Length % frameLength);
        Assert.InRange(painted, 1, frames);
        Assert.DoesNotContain("overflowing", await complaint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABroadcastCarryingTwoSoundsReachesALiveViewerAsOnePictureAndOneSound()
    {
        string written = await (SyntheticBroadcast.Sounding(SyntheticSound.TwoLanguages) with { Length = PastTheProbe })
            .WriteAsync(Path.Combine(room, "live-bilingual.m2ts"));

        IReadOnlyList<FfprobeRecord> tracks = await LiveTracksAsync(written, "live-bilingual.mp4");

        Assert.Equal(["video", "audio"], Types(tracks));
        Assert.Equal("LC", Sound(tracks).Value("profile"));
        Assert.Equal("2", Sound(tracks).Value("channels"));
        await BothTracksCarrySomethingAsync("live-bilingual.mp4");
    }

    [Fact]
    public async Task ASurroundBroadcastStillReachesALiveViewerWithEveryOneOfItsChannels()
    {
        string written = await (SyntheticBroadcast.Sounding(SyntheticSound.Surround) with { Length = PastTheProbe })
            .WriteAsync(Path.Combine(room, "live-surround.m2ts"));

        IReadOnlyList<FfprobeRecord> tracks = await LiveTracksAsync(written, "live-surround.mp4");

        Assert.Equal(["video", "audio"], Types(tracks));
        Assert.Equal("6", Sound(tracks).Value("channels"));
        Assert.Equal("5.1", Sound(tracks).Value("channel_layout"));
        await BothTracksCarrySomethingAsync("live-surround.mp4");
    }

    [Fact]
    public async Task ARecordingCarryingTwoSoundsIsPlayedBackWithItsMainOneAlone()
    {
        string written = await (SyntheticBroadcast.Sounding(SyntheticSound.TwoLanguages) with { Length = PastTheProbe })
            .WriteAsync(Path.Combine(room, "played-bilingual.m2ts"));

        string delivered = Path.Combine(room, "played-bilingual.mp4");

        await TranscodedAsync(
            [
                .. FfmpegPlaybackInvocation.Arguments(
                    Service,
                    LiveProfile.Hd30,
                    Interlaced,
                    LiveEncoder.Software,
                    new StreamSource(written),
                    TimeSpan.Zero),
                .. FfmpegLiveInvocation.DeliveryFromTheStart(),
            ],
            fed: null,
            delivered);

        IReadOnlyList<FfprobeRecord> tracks = await ProbedAsync(delivered);

        Assert.Equal(["video", "audio"], Types(tracks));
        Assert.Equal("LC", Sound(tracks).Value("profile"));
        Assert.Equal("2", Sound(tracks).Value("channels"));
        await BothTracksCarrySomethingAsync("played-bilingual.mp4");
    }

    [Fact]
    public async Task ASurroundRecordingIsPlayedBackWithEveryOneOfItsChannels()
    {
        string written = await (SyntheticBroadcast.Sounding(SyntheticSound.Surround) with { Length = PastTheProbe })
            .WriteAsync(Path.Combine(room, "played-surround.m2ts"));

        string delivered = Path.Combine(room, "played-surround.mp4");

        await TranscodedAsync(
            [
                .. FfmpegPlaybackInvocation.Arguments(
                    Service,
                    LiveProfile.Hd30,
                    Interlaced,
                    LiveEncoder.Software,
                    new StreamSource(written),
                    TimeSpan.Zero),
                .. FfmpegLiveInvocation.DeliveryFromTheStart(),
            ],
            fed: null,
            delivered);

        IReadOnlyList<FfprobeRecord> tracks = await ProbedAsync(delivered);

        Assert.Equal(["video", "audio"], Types(tracks));
        Assert.Equal("6", Sound(tracks).Value("channels"));
        Assert.Equal("5.1", Sound(tracks).Value("channel_layout"));
        await BothTracksCarrySomethingAsync("played-surround.mp4");
    }

    private async Task BothTracksCarrySomethingAsync(string name)
    {
        IReadOnlyList<FfprobeRecord> counted = await ProbedAsync(Path.Combine(room, name), Counted, "-count_packets");

        Assert.All(
            counted,
            record => Assert.True(
                int.Parse(record.Value("nb_read_packets")!, CultureInfo.InvariantCulture) > 0,
                $"the {record.Value("codec_type")} track holds no packet, so only a header was measured"));
    }

    private static string[] Types(IReadOnlyList<FfprobeRecord> probed)
        => [.. probed.Select(record => record.Value("codec_type")).OfType<string>()];

    private static FfprobeRecord Sound(IReadOnlyList<FfprobeRecord> probed)
        => probed.First(record => record.Value("codec_type") is "audio");

    private async Task<IReadOnlyList<FfprobeRecord>> LiveTracksAsync(string written, string name)
    {
        string delivered = Path.Combine(room, name);

        await TranscodedAsync(
            [
                .. FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software),
                .. FfmpegLiveInvocation.Delivery(),
            ],
            fed: written,
            delivered);

        return await ProbedAsync(delivered);
    }

    private static async Task TranscodedAsync(IReadOnlyList<string> arguments, string? fed, string delivered)
    {
        var start = new ProcessStartInfo(FfmpegProgramme.Default)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process running = Process.Start(start)!;

        Task feeding = Task.Run(async () =>
        {
            if (fed is not null)
            {
                await using FileStream source = File.OpenRead(fed);
                await source.CopyToAsync(running.StandardInput.BaseStream);
            }

            running.StandardInput.Close();
        });
        Task<string> complaint = running.StandardError.ReadToEndAsync();

        await using (FileStream held = File.Create(delivered))
        {
            await running.StandardOutput.BaseStream.CopyToAsync(held);
        }

        await feeding;
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode is 0, await complaint);
    }

    private static string[] Codecs(IReadOnlyList<FfprobeRecord> probed)
        => [.. probed.Select(record => record.Value("codec_name")).Where(codec => codec is not null).Distinct()!];

    private static string[] Programs(IReadOnlyList<FfprobeRecord> probed)
        => [.. probed.Select(record => record.Value("program_id")).Where(program => program is not null).Distinct()!];

    private static Task<StreamAttributeReading> ReadAsync(string path)
        => new FfprobeStreamAttributeReader(new StreamAttributeSettings(), TimeProvider.System)
            .ReadAsync(new StreamSource(path), CancellationToken.None);

    private static Task<IReadOnlyList<FfprobeRecord>> ProbedAsync(string path) => ProbedAsync(path, Entries);

    private static async Task<IReadOnlyList<FfprobeRecord>> ProbedAsync(string path, string entries, params string[] more)
    {
        var start = new ProcessStartInfo(Ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-of", FfprobeInvocation.Format, "-show_entries", entries, "-i", path,
        }.Concat(more))
        {
            start.ArgumentList.Add(argument);
        }

        using Process probing = Process.Start(start)!;
        Task<string> answer = probing.StandardOutput.ReadToEndAsync();
        Task<string> complaint = probing.StandardError.ReadToEndAsync();

        await probing.WaitForExitAsync();

        Assert.True(probing.ExitCode is 0, await complaint);

        return FfprobeRecords.From(await answer);
    }
}
