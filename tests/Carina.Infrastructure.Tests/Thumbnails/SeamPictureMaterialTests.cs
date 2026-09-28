using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

/// <summary>
/// Pictures taken out of a synthetic broadcast that was cut in the middle of a key frame and carried on in the
/// middle of a later group of pictures, the way a recording the driver resumed into is joined.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class SeamPictureMaterialTests : IDisposable
{
    private const int PacketSize = 188;

    private const int Width = 64;

    private const int Height = 36;

    private const double FurthestFromAFrameOfTheBroadcast = 2.0;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

    private readonly string room = Directory.CreateTempSubdirectory("carina-seam").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public async Task BrKd027AFrameAskedForAnywhereAroundTheSeamIsAFrameOfTheBroadcastAndNotTheOneJoinedAcrossIt()
    {
        Seam seam = await SeamAsync();
        FfmpegThumbnailRenderer renderer = new(new ThumbnailSettings { Width = 640 }, TimeProvider.System);
        List<string> broken = [];
        int drawn = 0;

        for (TimeSpan at = seam.CutAt - TimeSpan.FromSeconds(4); at <= seam.CarriedOnAt + TimeSpan.FromSeconds(4); at += Step)
        {
            TimeSpan taken = RecordingSeam.KeepClear(at, [new RecordingSeam(seam.CutAt, seam.CarriedOnAt)]);
            ThumbnailRender frame = await renderer.FrameAsync(new ThumbnailFrameRequest(seam.Joined, Service, taken), default);

            Assert.True(frame.Drew, $"no frame was drawn at {at.TotalSeconds:0.00}s: {frame.Note}");

            double distance = Nearest(await GreyAsync(frame.Picture!), seam.Frames);
            drawn++;

            if (distance > FurthestFromAFrameOfTheBroadcast)
            {
                broken.Add(FormattableString.Invariant($"{at.TotalSeconds:0.00}s is {distance:0.0} from every frame"));
            }
        }

        Assert.True(drawn >= 40, $"only {drawn} frames were drawn");
        Assert.Empty(broken);
    }

    [Fact]
    public async Task BrKd027ThePictureKeptForTheListIsAFrameOfTheBroadcastWhenItsPositionFallsOnTheSeam()
    {
        Seam seam = await SeamAsync();
        FfmpegThumbnailRenderer renderer = new(new ThumbnailSettings { Width = 640 }, TimeProvider.System);
        string destination = Path.Combine(room, "poster.jpg");

        ThumbnailRender drawn = await renderer.RenderAsync(
            new ThumbnailRequest(
                seam.Joined,
                destination,
                Service,
                RecordingSeam.KeepClear(seam.CutAt, [new RecordingSeam(seam.CutAt, seam.CarriedOnAt)])),
            default);

        Assert.True(drawn.Drew, drawn.Note);
        Assert.True(
            Nearest(await GreyAsync(await File.ReadAllBytesAsync(destination)), seam.Frames)
            <= FurthestFromAFrameOfTheBroadcast);
    }

    [Fact]
    public async Task TheSeamThisFileBuildsIsOneAPictureTakenWithoutKeepingClearOfItLandsOn()
    {
        Seam seam = await SeamAsync();
        string picture = Path.Combine(room, "unguarded.jpg");

        await FfmpegProgramme.RunAsync(
            FfmpegProgramme.Default,
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "quiet",
                "-y",
                "-ss",
                Seconds(seam.CutAt),
                "-i",
                seam.Joined,
                "-map",
                FormattableString.Invariant($"p:{Service.Value}:v:0"),
                "-frames:v",
                "1",
                picture,
            ],
            default);

        Assert.True(
            Nearest(await GreyAsync(await File.ReadAllBytesAsync(picture)), seam.Frames) > FurthestFromAFrameOfTheBroadcast,
            "the seam this test builds no longer produces a broken picture, so it no longer shows the guard working");
    }

    private async Task<Seam> SeamAsync()
    {
        string source = await new SyntheticBroadcast { Length = TimeSpan.FromSeconds(16) }
            .WriteAsync(Path.Combine(room, "broadcast" + SyntheticBroadcast.TransportStream));

        double start = double.Parse(
            (await RunAsync("ffprobe", ["-v", "error", "-show_entries", "format=start_time", "-of", "csv=p=0", source]))
                .Trim()
                .TrimEnd(','),
            CultureInfo.InvariantCulture);
        IReadOnlyList<VideoPacket> packets = await PacketsAsync(source);

        VideoPacket key = packets.First(packet => packet.Key && packet.Pts >= start + 5);
        long keyEnds = packets.First(packet => packet.Position > key.Position).Position;
        long cut = key.Position + ((keyEnds - key.Position) / PacketSize / 2 * PacketSize);
        VideoPacket resumed = packets.First(packet => !packet.Key && packet.Pts >= start + 8.5);
        long carriesOn = resumed.Position + (3 * PacketSize);

        byte[] whole = await File.ReadAllBytesAsync(source);
        string joined = Path.Combine(room, "joined" + SyntheticBroadcast.TransportStream);

        await File.WriteAllBytesAsync(joined, [.. whole.AsSpan(0, (int)cut), .. whole.AsSpan((int)carriesOn)]);

        byte[] frames = await RunBytesAsync(
            "ffmpeg",
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-i",
                source,
                "-map",
                FormattableString.Invariant($"p:{Service.Value}:v:0"),
                "-vf",
                FormattableString.Invariant($"scale={Width}:{Height},format=gray"),
                "-f",
                "rawvideo",
                "-",
            ]);

        return new Seam(
            joined,
            TimeSpan.FromSeconds(key.Pts - start),
            TimeSpan.FromSeconds(resumed.Pts - start),
            Split(frames));
    }

    private static async Task<IReadOnlyList<VideoPacket>> PacketsAsync(string source)
    {
        string listed = await RunAsync(
            "ffprobe",
            [
                "-v",
                "error",
                "-select_streams",
                FormattableString.Invariant($"p:{Service.Value}:v:0"),
                "-show_entries",
                "packet=pts_time,pos,flags",
                "-of",
                "csv=p=0",
                source,
            ]);

        return
        [
            .. listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(','))
                .Where(fields => fields.Length >= 3 && fields[0] is not "N/A" && fields[1] is not "N/A")
                .Select(fields => new VideoPacket(
                    double.Parse(fields[0], CultureInfo.InvariantCulture),
                    long.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields[2].Contains('K', StringComparison.Ordinal))),
        ];
    }

    private async Task<byte[]> GreyAsync(byte[] jpeg)
    {
        string picture = Path.Combine(room, Guid.NewGuid().ToString("N") + ".jpg");

        await File.WriteAllBytesAsync(picture, jpeg);

        return await RunBytesAsync(
            "ffmpeg",
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-i",
                picture,
                "-vf",
                FormattableString.Invariant($"scale={Width}:{Height},format=gray"),
                "-f",
                "rawvideo",
                "-",
            ]);
    }

    private static IReadOnlyList<byte[]> Split(byte[] frames)
    {
        const int size = Width * Height;

        return [.. Enumerable.Range(0, frames.Length / size).Select(index => frames.AsSpan(index * size, size).ToArray())];
    }

    private static double Nearest(byte[] picture, IReadOnlyList<byte[]> frames)
    {
        Assert.Equal(Width * Height, picture.Length);

        return frames.Min(frame => picture.Zip(frame, (left, right) => (double)Math.Abs(left - right)).Average());
    }

    private static string Seconds(TimeSpan at) => at.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static async Task<string> RunAsync(string programme, IReadOnlyList<string> arguments)
        => System.Text.Encoding.UTF8.GetString(await RunBytesAsync(programme, arguments));

    private static async Task<byte[]> RunBytesAsync(string programme, IReadOnlyList<string> arguments)
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
        using MemoryStream output = new();
        Task copied = running.StandardOutput.BaseStream.CopyToAsync(output);
        Task<string> complaint = running.StandardError.ReadToEndAsync();

        await running.WaitForExitAsync();
        await copied;

        Assert.True(running.ExitCode is 0, $"{programme} exited {running.ExitCode}: {await complaint}");

        return output.ToArray();
    }

    private sealed record VideoPacket(double Pts, long Position, bool Key);

    private sealed record Seam(string Joined, TimeSpan CutAt, TimeSpan CarriedOnAt, IReadOnlyList<byte[]> Frames);
}
