using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

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
                "color=black:s=256x256:r=1:d=1",
                "-vf",
                "format=nv12,hwupload",
                "-c:v",
                "h264_vaapi",
                "-f",
                "null",
                "-",
            ],
            VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode));
    }

    [Fact]
    public void ThePictureIsNoSmallerThanTheSmallestEitherCodecEncodesOnTheCard()
    {
        string[] arguments = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode, FfmpegFaculties.H265OnTheCard)];
        string picture = arguments[arguments.IndexOf("-i") + 1];
        string[] size = picture.Split(':')[1]["s=".Length..].Split('x');

        Assert.True(int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture) >= 128);
        Assert.True(int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture) >= 128);
    }

    [Fact]
    public void AskingWhetherTheCardWorksMeansEncodingAPictureOnIt()
    {
        string[] arguments = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode)];

        Assert.Equal("h264_vaapi", arguments[arguments.IndexOf("-c:v") + 1]);
        Assert.Contains("hwupload", arguments[arguments.IndexOf("-vf") + 1], StringComparison.Ordinal);
        Assert.True(arguments.IndexOf("-vaapi_device") < arguments.IndexOf("-i"));
    }

    [Fact(DisplayName = "asking about H.265 on the card is the same picture through hevc_vaapi, and nothing else changes")]
    public void AskingAboutH265OnTheCardIsTheSamePictureThroughHevcVaapi()
    {
        string[] h264 = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode)];
        string[] hevc = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode, FfmpegFaculties.H265OnTheCard)];

        Assert.Equal("hevc_vaapi", hevc[hevc.IndexOf("-c:v") + 1]);
        Assert.Equal(h264.Where(argument => argument is not "h264_vaapi"), hevc.Where(argument => argument is not "hevc_vaapi"));
        Assert.Throws<ArgumentException>(() => VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode, string.Empty));
    }

    [Fact(DisplayName = "asking about AV1 on the card is the same picture through av1_vaapi, no smaller than the card encodes AV1 at")]
    public void AskingAboutAv1OnTheCardIsTheSamePictureThroughAv1Vaapi()
    {
        string[] h264 = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode)];
        string[] av1 = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode, FfmpegFaculties.Av1OnTheCard)];
        string[] size = av1[av1.IndexOf("-i") + 1].Split(':')[1]["s=".Length..].Split('x');

        Assert.Equal("av1_vaapi", av1[av1.IndexOf("-c:v") + 1]);
        Assert.Equal(h264.Where(argument => argument is not "h264_vaapi"), av1.Where(argument => argument is not "av1_vaapi"));
        Assert.True(int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture) >= 128);
        Assert.True(int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture) >= 128);
    }

    [Fact]
    public void NoPictureIsDecodedOnTheCardEvenToAskWhetherItWorks()
    {
        string[] arguments = [.. VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode)];

        Assert.DoesNotContain("-hwaccel", arguments);
        Assert.DoesNotContain("-hwaccel_output_format", arguments);
        Assert.DoesNotContain("mpeg2_vaapi", arguments);
    }

    [Fact]
    public void NothingHandedToTheProgrammeIsMoreThanOneArgument()
    {
        Assert.All(
            VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode),
            argument =>
            {
                Assert.NotEqual(string.Empty, argument);
                Assert.DoesNotContain(argument, letter => char.IsWhiteSpace(letter) || char.IsControl(letter));
            });
    }

    [Fact]
    public void TheOnlyThingsHandedInAreTheNodeToAskAboutAndTheEncoderToAskWith()
    {
        Assert.Equal(
            ["renderNode", "renderNode,encoder"],
            typeof(VaapiProbeInvocation)
                .GetMethods()
                .Where(method => method.Name == nameof(VaapiProbeInvocation.Arguments))
                .Select(method => string.Join(',', method.GetParameters().Select(parameter => parameter.Name)))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ANodeThatIsNotThereToBeNamedIsNotSomethingToAskAbout()
        => Assert.Throws<ArgumentException>(() => VaapiProbeInvocation.Arguments(string.Empty));
}
