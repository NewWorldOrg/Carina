using System.Reflection;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfmpegCaptionInvocationTests
{
    private static readonly ServiceId Service = new(1040);

    private static readonly ServiceId AnotherService = new(1048);

    private static readonly VideoSize Terrestrial = new(1440, 1080);

    private static readonly VideoSize Satellite = new(1920, 1080);

    private static readonly VideoSize StandardDefinition = new(720, 480);

    [Fact]
    public void TheArgumentsAreExactlyThese()
    {
        Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "info",
                "-nostats",
                "-copyts",
                "-sub_type",
                "bitmap",
                "-canvas_size",
                "1440x1080",
                "-font",
                "Noto Sans CJK JP",
                "-i",
                "pipe:0",
                "-filter_complex",
                "[0:p:1040:s:0]format=bgra,showinfo[c]",
                "-map",
                "[c]",
                "-fps_mode",
                "passthrough",
            ],
            FfmpegCaptionInvocation.Arguments(Service, Terrestrial));
    }

    [Fact]
    public void TheDecoderIsToldWhichFontToDrawWithBeforeTheInputIsNamedAndItIsTheJapaneseFaceTheImageInstalls()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);

        Assert.Equal(1, arguments.Count(argument => argument == "-font"));
        Assert.Equal(FfmpegCaptionInvocation.Font, arguments[At(arguments, "-font") + 1]);
        Assert.Equal("Noto Sans CJK JP", FfmpegCaptionInvocation.Font);
        Assert.True(At(arguments, "-font") < At(arguments, "-i"));
    }

    [Fact]
    public void ThePicturesAreDeliveredRawToStandardOutputAndFlushedOneByOne()
    {
        Assert.Equal(["-flush_packets", "1", "-f", "rawvideo", "pipe:1"], FfmpegCaptionInvocation.Delivery());
    }

    [Theory]
    [InlineData(1440, 1080, "1440x1080")]
    [InlineData(1920, 1080, "1920x1080")]
    [InlineData(720, 480, "720x480")]
    public void TheCanvasIsTheMeasuredSizeOfThePictureAndAlwaysNamed(int width, int height, string canvas)
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, new VideoSize(width, height));

        Assert.Equal(canvas, arguments[At(arguments, "-canvas_size") + 1]);
        Assert.Equal(1, arguments.Count(argument => argument == "-canvas_size"));
    }

    [Fact]
    public void TheCanvasIsNamedBeforeTheInputSoItReachesTheDecoderOfThatInput()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);

        Assert.True(At(arguments, "-canvas_size") < At(arguments, "-i"));
        Assert.True(At(arguments, "-sub_type") < At(arguments, "-i"));
    }

    [Fact]
    public void ACanvasCannotBeAskedForByText()
    {
        MethodInfo[] building =
        [
            .. typeof(FfmpegCaptionInvocation)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name is nameof(FfmpegCaptionInvocation.Arguments)),
        ];

        Assert.Single(building);
        Assert.Equal(
            [typeof(ServiceId), typeof(VideoSize)],
            building[0].GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void OnlyTheFirstCaptionStreamOfTheNamedServiceIsDrawnAndNothingOfAnotherService()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);
        string graph = arguments[At(arguments, "-filter_complex") + 1];

        Assert.StartsWith("[0:p:1040:s:0]", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("0:s:0", arguments.Where(argument => argument != graph));
        Assert.NotEqual(
            FfmpegCaptionInvocation.Arguments(Service, Terrestrial),
            FfmpegCaptionInvocation.Arguments(AnotherService, Terrestrial));
    }

    [Fact]
    public void TheSourceClockIsKeptSoTheStampsMeetThePicturesOfTheOtherProcess()
    {
        Assert.Contains("-copyts", FfmpegCaptionInvocation.Arguments(Service, Terrestrial));
    }

    [Fact]
    public void EveryPictureIsPassedThroughAsItIsNeitherDuplicatedNorDropped()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);

        Assert.Equal("passthrough", arguments[At(arguments, "-fps_mode") + 1]);
    }

    [Fact]
    public void RepeatedPicturesAreLetThroughBecauseAPictureOnlyLeavesTheProgrammeWhenTheNextOneArrives()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);

        Assert.DoesNotContain("mpdecimate", arguments[At(arguments, "-filter_complex") + 1], StringComparison.Ordinal);
        Assert.DoesNotContain("decimate", arguments[At(arguments, "-filter_complex") + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheStampsAreAskedForAtTheLevelTheyArePrintedAtAndTheProgressLineIsNot()
    {
        IReadOnlyList<string> arguments = FfmpegCaptionInvocation.Arguments(Service, Terrestrial);

        Assert.Equal("info", arguments[At(arguments, "-loglevel") + 1]);
        Assert.Contains("-nostats", arguments);
        Assert.EndsWith("showinfo[c]", arguments[At(arguments, "-filter_complex") + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsEncodedAndNoBufferIsThrownAway()
    {
        IReadOnlyList<string> arguments = [.. FfmpegCaptionInvocation.Arguments(Service, Terrestrial), .. FfmpegCaptionInvocation.Delivery()];

        Assert.DoesNotContain("-c:v", arguments);
        Assert.DoesNotContain("-c:a", arguments);
        Assert.DoesNotContain("-vf", arguments);
        Assert.DoesNotContain("nobuffer", arguments);
        Assert.DoesNotContain("-vaapi_device", arguments);
    }

    [Fact]
    public void TheSameInputAsksForTheSameThingEveryTime()
    {
        Assert.Equal(
            FfmpegCaptionInvocation.Arguments(Service, Satellite),
            FfmpegCaptionInvocation.Arguments(Service, Satellite));
        Assert.NotEqual(
            FfmpegCaptionInvocation.Arguments(Service, Satellite),
            FfmpegCaptionInvocation.Arguments(Service, StandardDefinition));
    }

    [Fact]
    public void NothingIsBuiltWithoutAServiceOrACanvas()
    {
        Assert.Throws<ArgumentNullException>(() => FfmpegCaptionInvocation.Arguments(null!, Terrestrial));
        Assert.Throws<ArgumentNullException>(() => FfmpegCaptionInvocation.Arguments(Service, null!));
    }

    private static int At(IReadOnlyList<string> arguments, string named)
    {
        int at = arguments.ToList().IndexOf(named);

        Assert.True(at >= 0, $"{named} is not among the arguments");

        return at;
    }
}
