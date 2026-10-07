using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class FfmpegReducedCopyInvocationTests
{
    private const string Shelf = "/srv/copies/one:two";

    private static readonly IReadOnlyList<string> Arguments = FfmpegReducedCopyInvocation.Arguments(Copy());

    private static string Graph => Arguments[Arguments.ToList().IndexOf("-filter_complex") + 1];

    [Fact(DisplayName = "the sum, the difference, the frames and the tiles of the corners are each read as a file, never as a protocol, one thread a decoder")]
    public void EachFileIsReadAsAFileOneThreadADecoder()
    {
        string[] inputs = [.. Arguments.Select((argument, at) => (argument, at)).Where(pair => pair.argument is "-i").Select(pair => Arguments[pair.at + 1])];

        Assert.Equal(
            [.. new[] { ReducedCopy.Sum, ReducedCopy.Difference, ReducedCopy.Frames, ReducedCopy.Corners }.Select(file => "file:" + Path.Combine(Shelf, file))],
            inputs);
        Assert.All(
            Arguments.Select((argument, at) => (argument, at)).Where(pair => pair.argument is "-i"),
            pair => Assert.Equal(["-threads", "1"], Arguments.Skip(pair.at - 2).Take(2)));
    }

    [Fact(DisplayName = "the copy is read on its own clock, which is the recording's, and handed back as Matroska on the standard output, the filters on one thread")]
    public void TheCopyIsReadOnItsOwnClock()
    {
        Assert.Contains("-nostdin", Arguments);
        Assert.Contains("-copyts", Arguments);
        Assert.DoesNotContain("-start_at_zero", Arguments);
        Assert.Equal(["-filter_threads", "1"], Following("-filter_threads", 1));
        Assert.Equal(["-filter_complex_threads", "1"], Following("-filter_complex_threads", 1));
        Assert.True(Arguments.ToList().IndexOf("-filter_complex_threads") < Arguments.ToList().IndexOf("-filter_complex"));
        Assert.Equal(["-fps_mode", "passthrough"], Following("-fps_mode", 1));
        Assert.Equal(["-map", "[f]", "-map", "[c]", "-map", "[a]"], Following("-map", 5));
        Assert.Equal(["-c:v", "rawvideo", "-c:a", "pcm_s16le"], Following("-c:v", 3));
        Assert.Equal(["-f", "matroska", "pipe:1"], Arguments.TakeLast(3));
    }

    [Fact(DisplayName = "the two channels are put back from their sum and their difference, and come at the rate and in the samples the learning data reads")]
    public void TheTwoChannelsArePutBack()
        => Assert.Contains(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[0:a:0][1:a:0]join=inputs=2:channel_layout=stereo:map=0.0-FL|1.0-FR,pan=stereo|c0=c0+c1|c1=c0-c1,aresample={SoundReader.SampleRate},aformat=sample_fmts=s16:channel_layouts=stereo[a]"),
            Graph,
            StringComparison.Ordinal);

    [Fact(DisplayName = "the frames come in grey at every frame, and the tiles of the corners in grey at the first frame of each second, neither resized")]
    public void TheFramesAndTheTilesComeInGrey()
    {
        Assert.Contains("[2:v:0]format=gray[f]", Graph, StringComparison.Ordinal);
        Assert.Contains("[3:v:0]select=isnan(prev_selected_t)+gte(floor(t)\\,floor(prev_selected_t)+1),format=gray[c]", Graph, StringComparison.Ordinal);
        Assert.DoesNotContain("scale", Graph, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "nothing asks for the card")]
    public void NothingAsksForTheCard()
        => Assert.DoesNotContain(Arguments, argument => argument.Contains("vaapi", StringComparison.Ordinal) || argument.Contains("hwaccel", StringComparison.Ordinal));

    private static ReducedCopy Copy()
        => new(
            Shelf,
            RecordingId.New(),
            new ProgrammeCopy(
                new NetworkId(40001),
                new ServiceId(4321),
                CopyDescription.Starts,
                null,
                CopyDescription.Starts,
                "A programme",
                [],
                [],
                null,
                AudioMode.Stereo,
                null),
            TimeSpan.Zero,
            null);

    private static IReadOnlyList<string> Following(string option, int after)
    {
        int at = Arguments.ToList().IndexOf(option);

        Assert.True(at >= 0, $"{option} is not among the arguments");

        return [.. Arguments.Skip(at).Take(after + 1)];
    }
}
