using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class VaapiProbeInvocationTests
{
    [Fact]
    public void TheArgumentsAreExactlyThese()
    {
        Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-vaapi_device",
                "/dev/dri/renderD128",
                "-f",
                "lavfi",
                "-i",
                "color=black:s=64x64:r=1:d=1",
                "-vf",
                "format=nv12,hwupload",
                "-c:v",
                "h264_vaapi",
                "-f",
                "null",
                "-",
            ],
            VaapiProbeInvocation.Arguments());
    }

    [Fact]
    public void AskingWhetherTheCardWorksMeansEncodingAPictureOnIt()
    {
        string[] arguments = [.. VaapiProbeInvocation.Arguments()];

        Assert.Equal("h264_vaapi", arguments[arguments.IndexOf("-c:v") + 1]);
        Assert.Contains("hwupload", arguments[arguments.IndexOf("-vf") + 1], StringComparison.Ordinal);
        Assert.True(arguments.IndexOf("-vaapi_device") < arguments.IndexOf("-i"));
    }

    [Fact]
    public void NoPictureIsDecodedOnTheCardEvenToAskWhetherItWorks()
    {
        string[] arguments = [.. VaapiProbeInvocation.Arguments()];

        Assert.DoesNotContain("-hwaccel", arguments);
        Assert.DoesNotContain("-hwaccel_output_format", arguments);
        Assert.DoesNotContain("mpeg2_vaapi", arguments);
    }

    [Fact]
    public void NothingHandedToTheProgrammeIsMoreThanOneArgument()
    {
        Assert.All(
            VaapiProbeInvocation.Arguments(),
            argument =>
            {
                Assert.NotEqual(string.Empty, argument);
                Assert.DoesNotContain(argument, letter => char.IsWhiteSpace(letter) || char.IsControl(letter));
            });
    }

    [Fact]
    public void NoWayInTakesAnything()
    {
        Assert.Empty(typeof(VaapiProbeInvocation).GetMethod(nameof(VaapiProbeInvocation.Arguments))!.GetParameters());
    }
}
