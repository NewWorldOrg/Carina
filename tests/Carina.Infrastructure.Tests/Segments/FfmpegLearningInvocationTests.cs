using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class FfmpegLearningInvocationTests
{
    private static readonly TimeSpan HoursIntoTheDay = TimeSpan.FromTicks(512_345_678_910);

    private static readonly IReadOnlyList<string> Arguments = FfmpegLearningInvocation.Arguments(new ServiceId(1040), HoursIntoTheDay);

    private static string Graph => Arguments[Arguments.ToList().IndexOf("-filter_complex") + 1];

    [Fact(DisplayName = "the recording is read from the standard input on its own clock, one thread at a time, and handed back as Matroska on the standard output")]
    public void TheRecordingIsReadFromTheStandardInputOnItsOwnClock()
    {
        Assert.Equal(["-i", "pipe:0"], Following(Arguments, "-i", 1));
        Assert.Contains("-nostdin", Arguments);
        Assert.Contains("-copyts", Arguments);
        Assert.DoesNotContain("-start_at_zero", Arguments);
        Assert.DoesNotContain("-output_ts_offset", Arguments);
        Assert.Equal(["-threads", "1"], Following(Arguments, "-threads", 1));
        Assert.Equal(["-filter_threads", "1"], Following(Arguments, "-filter_threads", 1));
        Assert.Equal(["-filter_complex_threads", "1"], Following(Arguments, "-filter_complex_threads", 1));
        Assert.True(Arguments.ToList().IndexOf("-filter_complex_threads") < Arguments.ToList().IndexOf("-filter_complex"));
        Assert.Equal(["-fps_mode", "passthrough"], Following(Arguments, "-fps_mode", 1));
        Assert.Equal(["-f", "matroska", "pipe:1"], Arguments.TakeLast(3));
        Assert.True(Arguments.ToList().IndexOf("-copyts") < Arguments.ToList().IndexOf("-i"));
    }

    [Fact(DisplayName = "the clock is moved once, on the input, by the lift less where the file begins, to the microsecond")]
    public void TheClockIsMovedOnceOnTheInput()
    {
        Assert.Single(Arguments, argument => argument is "-itsoffset");
        Assert.True(Arguments.ToList().IndexOf("-itsoffset") < Arguments.ToList().IndexOf("-i"));
        Assert.Equal(["-itsoffset", "48765.432109"], Following(Arguments, "-itsoffset", 1));
        Assert.Equal(TimeSpan.FromSeconds(100_000), FfmpegLearningInvocation.Lift);
    }

    [Fact(DisplayName = "the picture is decoded at half its size, asked of the picture's decoder alone, before the input")]
    public void ThePictureIsDecodedAtHalfItsSize()
    {
        Assert.Equal(["-lowres:v", "1"], Following(Arguments, "-lowres:v", 1));
        Assert.True(Arguments.ToList().IndexOf("-lowres:v") < Arguments.ToList().IndexOf("-i"));
        Assert.DoesNotContain("-lowres", Arguments);
    }

    [Theory(DisplayName = "a moment ffmpeg reads is put on the recording's own time by taking off where the file begins, once, whether the file begins hours into the day, before zero because its clock comes around just after it begins, or with its clock coming around inside it")]
    [InlineData(51234.567891, 12.345678)]
    [InlineData(-2.6, 0.0)]
    [InlineData(-2.6, 5.0)]
    [InlineData(-59.5, 3600.25)]
    [InlineData(95_433.717688, 0.0)]
    [InlineData(95_433.717688, 30.0)]
    [InlineData(95_433.717688, 7200.5)]
    public void AMomentReadIsPutOnTheRecordingsOwnTime(double fileBegins, double intoTheRecording)
    {
        TimeSpan begins = TimeSpan.FromSeconds(fileBegins);
        TimeSpan read = begins + TimeSpan.FromSeconds(intoTheRecording);

        TimeSpan onTheRecording = FfmpegLearningInvocation.OnTheRecording(read + MovedBy(begins));

        Assert.InRange(onTheRecording.TotalSeconds, intoTheRecording - 1e-6, intoTheRecording + 1e-6);
        Assert.True(read + MovedBy(begins) > TimeSpan.Zero, "nothing ffmpeg hands back falls below zero");
    }

    [Fact(DisplayName = "a file read as beginning a whole turn of the clock away from zero, either side, is refused")]
    public void AFileBeginningAWholeTurnAwayIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegLearningInvocation.Arguments(new ServiceId(1040), -EncodeTimeline.OneTurnOfTheClock));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegLearningInvocation.Arguments(new ServiceId(1040), EncodeTimeline.OneTurnOfTheClock));
        _ = FfmpegLearningInvocation.Arguments(new ServiceId(1040), -EncodeTimeline.OneTurnOfTheClock + TimeSpan.FromTicks(1));
        _ = FfmpegLearningInvocation.Arguments(new ServiceId(1040), EncodeTimeline.OneTurnOfTheClock - TimeSpan.FromTicks(1));
    }

    [Fact(DisplayName = "the programme's first picture and first sound are taken by its service id")]
    public void TheFirstPictureAndSoundAreTakenByServiceId()
    {
        Assert.StartsWith("[0:p:1040:v:0]split=2", Graph, StringComparison.Ordinal);
        Assert.Contains(";[0:p:1040:a:0]pan=stereo|", Graph, StringComparison.Ordinal);
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

    [Fact(DisplayName = "the sound is folded to two channels by name before it is resampled, so channels a corrupt packet decodes into without names become silence instead of stopping ffmpeg")]
    public void TheSoundIsFoldedToTwoChannelsByNameBeforeItIsResampled()
    {
        Assert.Contains(
            string.Create(
                CultureInfo.InvariantCulture,
                $"pan=stereo|FL=FL+0.707*FC+0.707*BL+0.707*SL|FR=FR+0.707*FC+0.707*BR+0.707*SR,aresample={SoundReader.SampleRate},"),
            Graph,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "nothing asks for the card")]
    public void NothingAsksForTheCard()
        => Assert.DoesNotContain(Arguments, argument => argument.Contains("vaapi", StringComparison.Ordinal) || argument.Contains("hwaccel", StringComparison.Ordinal));

    private static TimeSpan MovedBy(TimeSpan fileBegins)
    {
        IReadOnlyList<string> arguments = FfmpegLearningInvocation.Arguments(new ServiceId(1040), fileBegins);
        string offset = Following(arguments, "-itsoffset", 1)[1];

        return TimeSpan.FromTicks((long)(decimal.Parse(offset, NumberStyles.Float, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond));
    }

    private static IReadOnlyList<string> Following(IReadOnlyList<string> arguments, string option, int after)
    {
        int at = arguments.ToList().IndexOf(option);

        Assert.True(at >= 0, $"{option} is not among the arguments");

        return [.. arguments.Skip(at).Take(after + 1)];
    }
}
