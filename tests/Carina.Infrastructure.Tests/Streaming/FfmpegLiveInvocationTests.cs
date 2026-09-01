using System.Globalization;
using System.Reflection;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfmpegLiveInvocationTests
{
    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private static readonly StreamAttributes Progressive = new(
        new VideoSize(1920, 1080),
        ScanType.Progressive,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    public static TheoryData<LiveProfile, LiveEncoder> EveryProfileOnEveryEncoder
    {
        get
        {
            var pairs = new TheoryData<LiveProfile, LiveEncoder>();

            foreach (LiveProfile profile in LiveProfile.All)
            {
                pairs.Add(profile, LiveEncoder.Software);
                pairs.Add(profile, LiveEncoder.Vaapi);
            }

            return pairs;
        }
    }

    [Fact]
    public void TheSoftwareArgumentsAreExactlyThese()
    {
        Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-fflags",
                "nobuffer",
                "-flags",
                "low_delay",
                "-copyts",
                "-i",
                "pipe:0",
                "-vf",
                "bwdif=mode=send_frame,scale=1280:720:flags=bicubic,setsar=1",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-tune",
                "zerolatency",
                "-g",
                "60",
                "-b:v",
                "3000k",
                "-maxrate",
                "3000k",
                "-bufsize",
                "6000k",
                "-c:a",
                "copy",
                "-bsf:a",
                "aac_adtstoasc",
            ],
            FfmpegLiveInvocation.Arguments(LiveProfile.Hd30, Interlaced, LiveEncoder.Software));
    }

    [Fact]
    public void TheVaapiArgumentsAreExactlyThese()
    {
        Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-fflags",
                "nobuffer",
                "-flags",
                "low_delay",
                "-copyts",
                "-vaapi_device",
                "/dev/dri/renderD128",
                "-i",
                "pipe:0",
                "-vf",
                "bwdif=mode=send_field,scale=1920:1080:flags=bicubic,setsar=1,format=nv12,hwupload",
                "-c:v",
                "h264_vaapi",
                "-g",
                "120",
                "-rc_mode",
                "CQP",
                "-qp",
                "24",
                "-c:a",
                "copy",
                "-bsf:a",
                "aac_adtstoasc",
            ],
            FfmpegLiveInvocation.Arguments(LiveProfile.FullHd60, Interlaced, LiveEncoder.Vaapi));
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void TheSameProfileAsksForTheSameThingEveryTime(LiveProfile profile, LiveEncoder encoder)
    {
        Assert.Equal(
            FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder),
            FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder));
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void EveryProfileAsksForSomethingOfItsOwn(LiveProfile profile, LiveEncoder encoder)
    {
        IEnumerable<LiveProfile> others = LiveProfile.All.Where(other => !ReferenceEquals(other, profile));

        Assert.All(
            others,
            other => Assert.NotEqual(
                FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder),
                FfmpegLiveInvocation.Arguments(other, Interlaced, encoder)));
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void NothingHandedToTheEncoderIsMoreThanOneArgument(LiveProfile profile, LiveEncoder encoder)
    {
        Assert.All(
            FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder),
            argument =>
            {
                Assert.NotEqual(string.Empty, argument);
                Assert.DoesNotContain(argument, letter => char.IsWhiteSpace(letter) || char.IsControl(letter));
            });
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void SoundIsCarriedOverRatherThanEncodedAgain(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        Assert.Equal("copy", arguments[arguments.IndexOf("-c:a") + 1]);
        Assert.Equal("aac_adtstoasc", arguments[arguments.IndexOf("-bsf:a") + 1]);
        Assert.DoesNotContain("-b:a", arguments);
        Assert.DoesNotContain("-ac", arguments);
        Assert.DoesNotContain("-ar", arguments);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void OnlyOneEncoderIsNamed(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        Assert.Equal(
            encoder is LiveEncoder.Vaapi ? "h264_vaapi" : "libx264",
            arguments[arguments.IndexOf("-c:v") + 1]);
        Assert.Single(arguments, argument => string.Equals(argument, "-c:v", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void TheRateControlGoesToTheEncoderThatHasIt(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        if (encoder is LiveEncoder.Vaapi)
        {
            Assert.Equal("CQP", arguments[arguments.IndexOf("-rc_mode") + 1]);
            Assert.Equal(
                profile.VaapiRateControl.Quantiser.ToString(CultureInfo.InvariantCulture),
                arguments[arguments.IndexOf("-qp") + 1]);
            Assert.DoesNotContain("-b:v", arguments);
            Assert.DoesNotContain("-maxrate", arguments);
            Assert.DoesNotContain("-bufsize", arguments);

            return;
        }

        Assert.Equal(profile.SoftwareRateControl.ToString(), arguments[arguments.IndexOf("-b:v") + 1]);
        Assert.Equal(profile.SoftwareRateControl.ToString(), arguments[arguments.IndexOf("-maxrate") + 1]);
        Assert.DoesNotContain("-rc_mode", arguments);
        Assert.DoesNotContain("-qp", arguments);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void ThePictureIsScaledToTheProfileAndGivenSquarePixels(LiveProfile profile, LiveEncoder encoder)
    {
        string filter = FilterOf(profile, Interlaced, encoder);

        Assert.Contains(
            $"scale={profile.Size.Width}:{profile.Size.Height}:flags=bicubic",
            filter,
            StringComparison.Ordinal);
        Assert.Contains("setsar=1", filter, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void OnlyTheHardwareEncoderIsHandedFramesAndADevice(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];
        string filter = FilterOf(profile, Interlaced, encoder);

        if (encoder is LiveEncoder.Vaapi)
        {
            Assert.Equal("/dev/dri/renderD128", arguments[arguments.IndexOf("-vaapi_device") + 1]);
            Assert.True(arguments.IndexOf("-vaapi_device") < arguments.IndexOf("-i"));
            Assert.EndsWith("format=nv12,hwupload", filter, StringComparison.Ordinal);

            return;
        }

        Assert.DoesNotContain("-vaapi_device", arguments);
        Assert.DoesNotContain("hwupload", filter, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void NoDecoderIsAskedForAtAll(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        Assert.DoesNotContain("-hwaccel", arguments);
        Assert.DoesNotContain("-hwaccel_output_format", arguments);
        Assert.DoesNotContain("-c:v:0", arguments);
        Assert.DoesNotContain("mpeg2_vaapi", arguments);
    }

    [Theory]
    [InlineData("1080p60", "send_field")]
    [InlineData("1080p30", "send_frame")]
    [InlineData("720p60", "send_field")]
    [InlineData("720p30", "send_frame")]
    public void AProfileWantingEveryFieldSeparatesThem(string name, string mode)
    {
        Assert.StartsWith(
            $"bwdif=mode={mode},",
            FilterOf(LiveProfile.Find(name)!, Interlaced, LiveEncoder.Software),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStreamThatSaysItIsProgressiveIsNotDeinterlaced()
    {
        Assert.DoesNotContain(
            "bwdif",
            FilterOf(LiveProfile.Hd30, Progressive, LiveEncoder.Software),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStreamThatSaysNothingAboutItsScanIsDeinterlacedAnyway()
    {
        var unsaid = new StreamAttributes(
            new VideoSize(1440, 1080),
            ScanType.Undetermined,
            FrameRate.BroadcastFrames,
            AudioMode.Stereo);

        Assert.Contains(
            "bwdif",
            FilterOf(LiveProfile.Hd30, unsaid, LiveEncoder.Software),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void AKeyframeArrivesEveryTwoSecondsOfTheProfilesOwnFrames(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        Assert.Equal(
            Math.Round(profile.Rate.PerSecond * 2).ToString(CultureInfo.InvariantCulture),
            arguments[arguments.IndexOf("-g") + 1]);
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void NoSizeIsPutOnACanvasNobodyHasMeasured(LiveProfile profile, LiveEncoder encoder)
    {
        Assert.DoesNotContain("-canvas_size", FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder));
    }

    [Theory]
    [MemberData(nameof(EveryProfileOnEveryEncoder))]
    public void NothingIsSaidAboutWhereTheAnswerGoes(LiveProfile profile, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, Interlaced, encoder)];

        Assert.DoesNotContain("-f", arguments);
        Assert.DoesNotContain("-movflags", arguments);
        Assert.DoesNotContain("-frag_duration", arguments);
        Assert.DoesNotContain("pipe:1", arguments);
    }

    [Fact]
    public void TheOnePlaceThatSaysWhereTheAnswerGoesSaysExactlyThis()
    {
        Assert.Equal(
            [
                "-f",
                "mp4",
                "-movflags",
                "empty_moov+default_base_moof",
                "-frag_duration",
                "200000",
                "pipe:1",
            ],
            FfmpegLiveInvocation.Delivery());
    }

    [Fact]
    public void TheAnswerIsAContainerThatCanBeWrittenToSomethingThatCannotBeWoundBack()
    {
        string[] delivery = [.. FfmpegLiveInvocation.Delivery()];

        Assert.Equal("mp4", delivery[delivery.IndexOf("-f") + 1]);
        Assert.Contains("empty_moov", delivery[delivery.IndexOf("-movflags") + 1], StringComparison.Ordinal);
        Assert.Contains("default_base_moof", delivery[delivery.IndexOf("-movflags") + 1], StringComparison.Ordinal);
        Assert.Equal(FfmpegLiveInvocation.Output, delivery[^1]);
    }

    [Fact]
    public void TheAnswerIsCutEveryFifthOfASecond()
    {
        string[] delivery = [.. FfmpegLiveInvocation.Delivery()];

        Assert.Equal("200000", delivery[delivery.IndexOf("-frag_duration") + 1]);
    }

    [Fact]
    public void TheAnswerIsNotCutAtEveryFrame()
    {
        string[] delivery = [.. FfmpegLiveInvocation.Delivery()];

        Assert.DoesNotContain("-frag_every_frame", delivery);
        Assert.DoesNotContain("frag_every_frame", delivery[delivery.IndexOf("-movflags") + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEncoderThatIsNotOneOfTheTwoIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegLiveInvocation.Arguments(LiveProfile.Hd30, Interlaced, (LiveEncoder)7));
    }

    [Fact]
    public void ThereAreOnlyTheTwoEncodersTheBenchmarkCompared()
    {
        Assert.Equal([LiveEncoder.Software, LiveEncoder.Vaapi], Enum.GetValues<LiveEncoder>());
    }

    [Fact]
    public void NoWayInTakesText()
    {
        Assert.Empty(TextTakenBy(typeof(FfmpegLiveInvocation)));
    }

    [Fact]
    public void TheCheckOnTheWayInReadsConstructorsAndNotStaticFactories()
    {
        Assert.Contains(
            "TextTakingFixture(String)",
            TextTakenBy(typeof(TextTakingFixture)),
            StringComparer.Ordinal);

        Assert.NotNull(typeof(FrameRate).GetMethod(nameof(FrameRate.Read), [typeof(string)]));
        Assert.Null(FrameRate.Read("thirty"));
    }

    private static IReadOnlyList<string> TextTakenBy(Type builder)
    {
        List<string> taking = [];
        HashSet<Type> seen = [];

        foreach (MethodInfo method in builder.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Reach(parameter.ParameterType, $"{builder.Name}.{method.Name}({parameter.Name})", taking, seen);
            }
        }

        foreach (ConstructorInfo constructor in builder.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Reach(parameter.ParameterType, $"{builder.Name}({parameter.ParameterType.Name})", taking, seen);
            }
        }

        return taking;
    }

    private static void Reach(Type type, string named, List<string> taking, HashSet<Type> seen)
    {
        if (type == typeof(string))
        {
            taking.Add(named);

            return;
        }

        if (type.IsEnum || type.IsPrimitive || !seen.Add(type))
        {
            return;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Reach(parameter.ParameterType, $"{type.Name}({parameter.ParameterType.Name})", taking, seen);
            }
        }
    }

    private static string FilterOf(LiveProfile profile, StreamAttributes attributes, LiveEncoder encoder)
    {
        string[] arguments = [.. FfmpegLiveInvocation.Arguments(profile, attributes, encoder)];

        return arguments[arguments.IndexOf("-vf") + 1];
    }
}

public sealed class TextTakingFixture
{
    public TextTakingFixture(string filter) => Filter = filter;

    public string Filter { get; }
}
