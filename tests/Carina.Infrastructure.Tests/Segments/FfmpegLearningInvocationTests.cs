using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class FfmpegLearningInvocationTests
{
    private static readonly IReadOnlyList<string> Arguments = FfmpegLearningInvocation.Arguments(new ServiceId(1040));

    private static string Graph => Arguments[Arguments.ToList().IndexOf("-filter_complex") + 1];

    [Fact(DisplayName = "the recording is read from the standard input on its own clock, one thread at a time, and handed back as Matroska on the standard output")]
    public void TheRecordingIsReadFromTheStandardInputOnItsOwnClock()
    {
        Assert.Equal(["-i", "pipe:0"], Following(Arguments, "-i", 1));
        Assert.Contains("-nostdin", Arguments);
        Assert.Contains("-copyts", Arguments);
        Assert.Contains("-start_at_zero", Arguments);
        Assert.Equal(["-threads", "1"], Following(Arguments, "-threads", 1));
        Assert.Equal(["-filter_threads", "1"], Following(Arguments, "-filter_threads", 1));
        Assert.Equal(["-fps_mode", "passthrough"], Following(Arguments, "-fps_mode", 1));
        Assert.Equal(["-f", "matroska", "pipe:1"], Arguments.TakeLast(3));
        Assert.True(Arguments.ToList().IndexOf("-copyts") < Arguments.ToList().IndexOf("-i"));
    }

    [Fact(DisplayName = "the programme's first picture and first sound are taken by its service id")]
    public void TheFirstPictureAndSoundAreTakenByServiceId()
    {
        Assert.StartsWith("[0:p:1040:v:0]split=2", Graph, StringComparison.Ordinal);
        Assert.Contains(";[0:p:1040:a:0]aresample", Graph, StringComparison.Ordinal);
        Assert.Equal(["-map", "[f]", "-map", "[c]", "-map", "[a]"], Following(Arguments, "-map", 5));
        Assert.Equal(["-c:v", "rawvideo", "-c:a", "pcm_s16le"], Following(Arguments, "-c:v", 3));
    }

    [Fact(DisplayName = "the frames, the pictures of each second and the sound come in the shapes the learning data reads")]
    public void TheShapesAreTheOnesTheLearningDataReads()
    {
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"scale={FrameLight.Width}:{FrameLight.Height}:flags=area,format=gray[f]"),
            Graph,
            StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"scale={WatermarkFrame.Width}:{WatermarkFrame.Height}:flags=area,format=gray[c]"),
            Graph,
            StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"aresample={SoundReader.SampleRate},aformat=sample_fmts=s16:channel_layouts=stereo[a]"),
            Graph,
            StringComparison.Ordinal);
        Assert.Contains("select=isnan(prev_selected_t)+gte(floor(t)\\,floor(prev_selected_t)+1)", Graph, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "nothing asks for the card")]
    public void NothingAsksForTheCard()
        => Assert.DoesNotContain(Arguments, argument => argument.Contains("vaapi", StringComparison.Ordinal) || argument.Contains("hwaccel", StringComparison.Ordinal));

    private static IReadOnlyList<string> Following(IReadOnlyList<string> arguments, string option, int after)
    {
        int at = arguments.ToList().IndexOf(option);

        Assert.True(at >= 0, $"{option} is not among the arguments");

        return [.. arguments.Skip(at).Take(after + 1)];
    }
}
