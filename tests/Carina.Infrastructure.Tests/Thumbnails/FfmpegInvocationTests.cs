using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class FfmpegInvocationTests
{
    private const int StepsFromPerfect = 3;

    private static readonly ThumbnailRequest Request =
        new("/srv/recordings/a.m2ts", "/srv/thumbnails/a.jpg", new ServiceId(1032), TimeSpan.FromSeconds(120));

    private static readonly ThumbnailFrameRequest Frame =
        new("/srv/recordings/a.m2ts", new ServiceId(1032), TimeSpan.FromSeconds(42));

    [Fact]
    public void TheSeekComesBeforeTheInputBecauseAfterItTheWholeFileIsDecoded()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        int seek = Index(arguments, "-ss");
        int input = Index(arguments, "-i");

        Assert.True(seek < input, $"-ss sits at {seek} and -i at {input}");
        Assert.Equal("120", arguments[seek + 1]);
        Assert.Equal("/srv/recordings/a.m2ts", arguments[input + 1]);
    }

    [Fact]
    public void ThePositionIsWrittenInSecondsAndKeepsItsFraction()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(
            new ThumbnailRequest("/a.m2ts", "/a.jpg", new ServiceId(1032), TimeSpan.FromSeconds(90.5)),
            960,
            StepsFromPerfect);

        Assert.Equal("90.5", arguments[Index(arguments, "-ss") + 1]);
    }

    [Fact]
    public void TheHeightIsWorkedOutFromTheDisplayAspectRatioAndNotFromTheStoredPixels()
    {
        string filter = ScrubFilter(960);

        Assert.Equal("scale=960:trunc(960/dar/2)*2:flags=bicubic,setsar=1", filter);
        Assert.DoesNotContain("scale=960:-2", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWidthItIsGivenIsTheWidthItAsksFor()
        => Assert.Equal("scale=640:trunc(640/dar/2)*2:flags=bicubic,setsar=1", ScrubFilter(640));

    [Fact]
    public void BrKd012ThePictureKeptForTheListIsTheMostTypicalOfTheFramesFromThePositionOnwards()
    {
        Assert.Equal(
            "thumbnail=100,scale=960:trunc(960/dar/2)*2:flags=bicubic,setsar=1",
            PosterFilter(960));
    }

    [Fact]
    public void BrKd012TheFramesItLooksAtSpanLongerThanATransitionAndShorterThanAShot()
    {
        Assert.Equal(100, FfmpegInvocation.FramesLookedAt);
    }

    [Fact]
    public void BrKd012ItLooksForATypicalFrameBeforeItScalesOneBecauseOnlyOneFrameIsWorthScaling()
    {
        string filter = PosterFilter(960);

        Assert.StartsWith("thumbnail=", filter, StringComparison.Ordinal);
        Assert.True(
            filter.IndexOf("thumbnail=", StringComparison.Ordinal)
            < filter.IndexOf("scale=", StringComparison.Ordinal),
            filter);
    }

    [Fact]
    public void BrKd012TheFrameAskedForOnDemandIsTheOneAtThePositionAndIsNotSwappedForATypicalOne()
    {
        Assert.DoesNotContain("thumbnail=", ScrubFilter(960), StringComparison.Ordinal);
    }

    [Fact]
    public void BrKd012BothPicturesAreScaledTheSameWayAndOnlyTheChoosingDiffers()
    {
        Assert.NotEqual(ScrubFilter(960), PosterFilter(960));
        Assert.EndsWith(ScrubFilter(960), PosterFilter(960), StringComparison.Ordinal);
    }

    [Fact]
    public void OneFrameIsAskedForAndItGoesWhereTheRequestSays()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        Assert.Equal("1", arguments[Index(arguments, "-frames:v") + 1]);
        Assert.Equal("/srv/thumbnails/a.jpg", arguments[^1]);
    }

    [Fact]
    public void NothingIsReadFromTheTerminalBecauseNobodyIsThere()
        => Assert.Contains("-nostdin", FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect), StringComparer.Ordinal);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-960)]
    [InlineData(961)]
    public void AWidthNoPictureCouldHaveIsRefused(int width)
        => Assert.Equal(
            "width",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FfmpegInvocation.Arguments(Request, width, StepsFromPerfect)).ParamName);

    [Fact]
    public void TheNarrowestPictureThereCanBeIsStillAccepted()
        => Assert.Equal("scale=2:trunc(2/dar/2)*2:flags=bicubic,setsar=1", ScrubFilter(2));

    [Fact]
    public void TheVideoStreamIsTheRecordedServicesOwnAndNotWhicheverFfmpegLikesBest()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        Assert.Equal("p:1032:v:0", arguments[Index(arguments, "-map") + 1]);
    }

    [Theory]
    [InlineData(0, "p:0:v:0")]
    [InlineData(1024, "p:1024:v:0")]
    [InlineData(23610, "p:23610:v:0")]
    [InlineData(65535, "p:65535:v:0")]
    public void TheProgrammeIsNamedByTheServiceIdAndNothingElseReachesTheArgument(int service, string expected)
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(
            new ThumbnailRequest("/a.m2ts", "/a.jpg", new ServiceId(service), TimeSpan.Zero),
            960,
            StepsFromPerfect);

        Assert.Equal(expected, arguments[Index(arguments, "-map") + 1]);
    }

    [Fact]
    public void TheStreamIsNamedAfterTheInputBecauseItIsTheInputItSelectsFrom()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        Assert.True(Index(arguments, "-i") < Index(arguments, "-map"));
    }

    [Fact]
    public void NoRequestMeansNoArguments()
        => Assert.Equal(
            "request",
            Assert.Throws<ArgumentNullException>(() => FfmpegInvocation.Arguments(null!, 960, StepsFromPerfect)).ParamName);

    [Fact]
    public void AFrameAskedForOnDemandIsHandedBackThroughThePipeAndLeavesNoFileBehind()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.FrameArguments(Frame, 960, StepsFromPerfect);

        Assert.Equal("-", arguments[^1]);
        Assert.Equal("image2pipe", arguments[Index(arguments, "-f") + 1]);
        Assert.Equal("mjpeg", arguments[Index(arguments, "-c:v") + 1]);
        Assert.DoesNotContain(arguments, argument => argument.EndsWith(".jpg", StringComparison.Ordinal));
    }

    [Fact]
    public void AFrameIsReadTheSameWayAStoredPictureIs()
    {
        IReadOnlyList<string> frame = FfmpegInvocation.FrameArguments(Frame, 960, StepsFromPerfect);

        Assert.True(Index(frame, "-ss") < Index(frame, "-i"));
        Assert.Equal("42", frame[Index(frame, "-ss") + 1]);
        Assert.Equal("p:1032:v:0", frame[Index(frame, "-map") + 1]);
        Assert.Equal("scale=960:trunc(960/dar/2)*2:flags=bicubic,setsar=1", frame[Index(frame, "-vf") + 1]);
        Assert.Equal("1", frame[Index(frame, "-frames:v") + 1]);
    }

    [Fact]
    public void NoFrameRequestMeansNoArguments()
        => Assert.Equal(
            "request",
            Assert.Throws<ArgumentNullException>(
                () => FfmpegInvocation.FrameArguments(null!, 960, StepsFromPerfect)).ParamName);

    [Theory]
    [InlineData(0)]
    [InlineData(961)]
    public void AFrameOfAWidthNoPictureCouldHaveIsRefused(int width)
        => Assert.Equal(
            "width",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FfmpegInvocation.FrameArguments(Frame, width, StepsFromPerfect)).ParamName);

    [Fact]
    public void ThePictureIsWrittenAtTheQualityItIsToldAndNotAtWhateverTheProgrammeWouldPick()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        Assert.Equal("3", arguments[Index(arguments, "-q:v") + 1]);
    }

    [Fact]
    public void TheQualityBelongsToTheOutputAndSoComesAfterTheInputAndBeforeTheDestination()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, StepsFromPerfect);

        int input = Index(arguments, "-i");
        int quality = Index(arguments, "-q:v");

        Assert.True(input < quality, $"-i sits at {input} and -q:v at {quality}");
        Assert.True(quality + 1 < arguments.Count - 1, $"-q:v sits at {quality} of {arguments.Count}");
    }

    [Fact]
    public void AFrameAskedForOnDemandIsWrittenAtTheQualityItIsToldToo()
    {
        IReadOnlyList<string> frame = FfmpegInvocation.FrameArguments(Frame, 960, 7);

        Assert.Equal("7", frame[Index(frame, "-q:v") + 1]);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    public void AQualityMjpegCannotDrawIsRefused(int stepsFromPerfect)
    {
        Assert.Equal(
            "stepsFromPerfect",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FfmpegInvocation.Arguments(Request, 960, stepsFromPerfect)).ParamName);

        Assert.Equal(
            "stepsFromPerfect",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FfmpegInvocation.FrameArguments(Frame, 960, stepsFromPerfect)).ParamName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(31)]
    public void TheBestAndTheCoarsestPicturesMjpegDrawsAreBothAccepted(int stepsFromPerfect)
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960, stepsFromPerfect);

        Assert.Equal(
            stepsFromPerfect.ToString(CultureInfo.InvariantCulture),
            arguments[Index(arguments, "-q:v") + 1]);
    }

    private static string PosterFilter(int width)
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, width, StepsFromPerfect);

        return arguments[Index(arguments, "-vf") + 1];
    }

    private static string ScrubFilter(int width)
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.FrameArguments(Frame, width, StepsFromPerfect);

        return arguments[Index(arguments, "-vf") + 1];
    }

    private static int Index(IReadOnlyList<string> arguments, string flag)
    {
        for (int at = 0; at < arguments.Count; at++)
        {
            if (string.Equals(arguments[at], flag, StringComparison.Ordinal))
            {
                return at;
            }
        }

        throw new InvalidOperationException($"The invocation carries no {flag}: {string.Join(' ', arguments)}");
    }
}
