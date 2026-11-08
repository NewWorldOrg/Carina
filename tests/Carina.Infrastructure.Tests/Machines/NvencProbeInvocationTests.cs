using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

public sealed class NvencProbeInvocationTests
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
                "-init_hw_device",
                "cuda=cu",
                "-filter_hw_device",
                "cu",
                "-f",
                "lavfi",
                "-i",
                "color=black:s=256x256:r=1:d=1",
                "-vf",
                "format=nv12,hwupload_cuda",
                "-c:v",
                "h264_nvenc",
                "-f",
                "null",
                "-",
            ],
            NvencProbeInvocation.Arguments());
    }

    [Fact(DisplayName = "the picture is the one the VAAPI probe sends, so the two cards are asked the same question")]
    public void ThePictureIsTheOneTheVaapiProbeSends()
    {
        string[] nvenc = [.. NvencProbeInvocation.Arguments()];
        string[] vaapi = [.. VaapiProbeInvocation.Arguments("/dev/dri/renderD128")];

        Assert.Equal(vaapi[vaapi.IndexOf("-i") + 1], nvenc[nvenc.IndexOf("-i") + 1]);
    }

    [Fact(DisplayName = "the CUDA device is made before the input and the filters are told its name")]
    public void TheCudaDeviceIsMadeBeforeTheInputAndTheFiltersAreToldItsName()
    {
        string[] arguments = [.. NvencProbeInvocation.Arguments()];

        Assert.True(arguments.IndexOf("-init_hw_device") < arguments.IndexOf("-i"));
        Assert.Equal("cuda=cu", arguments[arguments.IndexOf("-init_hw_device") + 1]);
        Assert.Equal("cu", arguments[arguments.IndexOf("-filter_hw_device") + 1]);
        Assert.EndsWith("hwupload_cuda", arguments[arguments.IndexOf("-vf") + 1], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "no render node is named, because an NVIDIA card is not reached through one")]
    public void NoRenderNodeIsNamed()
    {
        Assert.DoesNotContain("-vaapi_device", NvencProbeInvocation.Arguments());
        Assert.DoesNotContain(NvencProbeInvocation.Arguments(), argument => argument.Contains("/dev/", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "asking about H.265 or AV1 is the same picture through that encoder, and nothing else changes")]
    [InlineData("hevc_nvenc")]
    [InlineData("av1_nvenc")]
    public void AskingAboutAnotherFormatIsTheSamePictureThroughThatEncoder(string encoder)
    {
        string[] h264 = [.. NvencProbeInvocation.Arguments()];
        string[] other = [.. NvencProbeInvocation.Arguments(encoder)];

        Assert.Equal(encoder, other[other.IndexOf("-c:v") + 1]);
        Assert.Equal(h264.Where(argument => argument is not "h264_nvenc"), other.Where(argument => argument != encoder));
    }

    [Fact]
    public void NoPictureIsDecodedOnTheCardEvenToAskWhetherItWorks()
    {
        string[] arguments = [.. NvencProbeInvocation.Arguments()];

        Assert.DoesNotContain("-hwaccel", arguments);
        Assert.DoesNotContain("-hwaccel_output_format", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("cuvid", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingHandedToTheProgrammeIsMoreThanOneArgument()
    {
        Assert.All(
            NvencProbeInvocation.Arguments(),
            argument =>
            {
                Assert.NotEqual(string.Empty, argument);
                Assert.DoesNotContain(argument, letter => char.IsWhiteSpace(letter) || char.IsControl(letter));
            });
    }

    [Fact]
    public void TheOnlyThingHandedInIsTheEncoderToAskWith()
    {
        Assert.Equal(
            [string.Empty, "encoder"],
            typeof(NvencProbeInvocation)
                .GetMethods()
                .Where(method => method.Name == nameof(NvencProbeInvocation.Arguments))
                .Select(method => string.Join(',', method.GetParameters().Select(parameter => parameter.Name)))
                .Order(StringComparer.Ordinal));
        Assert.Throws<ArgumentException>(() => NvencProbeInvocation.Arguments(string.Empty));
    }
}
