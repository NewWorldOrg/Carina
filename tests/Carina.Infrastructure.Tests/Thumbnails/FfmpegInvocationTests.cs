using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class FfmpegInvocationTests
{
    private static readonly ThumbnailRequest Request =
        new("/srv/recordings/a.m2ts", "/srv/thumbnails/a.jpg", TimeSpan.FromSeconds(120));

    [Fact]
    public void TheSeekComesBeforeTheInputBecauseAfterItTheWholeFileIsDecoded()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960);

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
            new ThumbnailRequest("/a.m2ts", "/a.jpg", TimeSpan.FromSeconds(90.5)),
            960);

        Assert.Equal("90.5", arguments[Index(arguments, "-ss") + 1]);
    }

    [Fact]
    public void TheHeightIsWorkedOutFromTheDisplayAspectRatioAndNotFromTheStoredPixels()
    {
        string filter = Filter(960);

        Assert.Equal("scale=960:trunc(960/dar/2)*2:flags=bicubic,setsar=1", filter);
        Assert.DoesNotContain("scale=960:-2", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWidthItIsGivenIsTheWidthItAsksFor()
        => Assert.Equal("scale=640:trunc(640/dar/2)*2:flags=bicubic,setsar=1", Filter(640));

    [Fact]
    public void OneFrameIsAskedForAndItGoesWhereTheRequestSays()
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, 960);

        Assert.Equal("1", arguments[Index(arguments, "-frames:v") + 1]);
        Assert.Equal("/srv/thumbnails/a.jpg", arguments[^1]);
    }

    [Fact]
    public void NothingIsReadFromTheTerminalBecauseNobodyIsThere()
        => Assert.Contains("-nostdin", FfmpegInvocation.Arguments(Request, 960), StringComparer.Ordinal);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-960)]
    [InlineData(961)]
    public void AWidthNoPictureCouldHaveIsRefused(int width)
        => Assert.Equal(
            "width",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FfmpegInvocation.Arguments(Request, width)).ParamName);

    [Fact]
    public void TheNarrowestPictureThereCanBeIsStillAccepted()
        => Assert.Equal("scale=2:trunc(2/dar/2)*2:flags=bicubic,setsar=1", Filter(2));

    [Fact]
    public void NoRequestMeansNoArguments()
        => Assert.Equal(
            "request",
            Assert.Throws<ArgumentNullException>(() => FfmpegInvocation.Arguments(null!, 960)).ParamName);

    private static string Filter(int width)
    {
        IReadOnlyList<string> arguments = FfmpegInvocation.Arguments(Request, width);

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
