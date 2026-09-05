using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegEncodeInvocationTests
{
    private const string Source = "/srv/recordings/0f8c.ts";

    private const string Destination = "/srv/encoded/0f8c.mp4";

    private static readonly ServiceId Service = new(1040);

    private const int Cores = 2;

    private static readonly DateTime At = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    public static TheoryData<EncodeCodec, EncodeResolution, Deinterlace, EncodeEncoder> EveryShapeOnEveryEncoder
    {
        get
        {
            var shapes = new TheoryData<EncodeCodec, EncodeResolution, Deinterlace, EncodeEncoder>();

            foreach (EncodeCodec codec in Enum.GetValues<EncodeCodec>())
            {
                foreach (EncodeResolution resolution in Enum.GetValues<EncodeResolution>())
                {
                    foreach (Deinterlace deinterlace in Enum.GetValues<Deinterlace>())
                    {
                        foreach (EncodeEncoder encoder in Enum.GetValues<EncodeEncoder>())
                        {
                            shapes.Add(codec, resolution, deinterlace, encoder);
                        }
                    }
                }
            }

            return shapes;
        }
    }

    private static EncodeProfile Profile(
        EncodeCodec codec = EncodeCodec.H264,
        EncodeResolution resolution = EncodeResolution.AsSource,
        Deinterlace deinterlace = Deinterlace.EveryFrame)
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            codec,
            resolution,
            deinterlace,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);

    [Fact]
    public void TheSoftwareArgumentsAreExactlyThese()
        => Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-nostats",
                "-progress",
                "pipe:1",
                "-y",
                "-filter_threads",
                "2",
                "-threads",
                "2",
                "-i",
                Source,
                "-map",
                "p:1040:v:0",
                "-map",
                "p:1040:a:0",
                "-vf",
                "bwdif=mode=send_frame,setsar=1",
                "-c:v",
                "libx264",
                "-preset",
                "medium",
                "-crf",
                "22",
                "-threads",
                "2",
                "-c:a",
                "copy",
                "-bsf:a",
                "aac_adtstoasc",
            ],
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores));

    [Fact]
    public void TheCardsArgumentsAreExactlyThese()
        => Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-nostats",
                "-progress",
                "pipe:1",
                "-y",
                "-filter_threads",
                "2",
                "-vaapi_device",
                FfmpegEncodeInvocation.RenderNode,
                "-threads",
                "2",
                "-i",
                Source,
                "-map",
                "p:1040:v:0",
                "-map",
                "p:1040:a:0",
                "-vf",
                "bwdif=mode=send_frame,setsar=1,format=nv12,hwupload",
                "-c:v",
                "h264_vaapi",
                "-rc_mode",
                "CQP",
                "-qp",
                "24",
                "-threads",
                "2",
                "-c:a",
                "copy",
                "-bsf:a",
                "aac_adtstoasc",
            ],
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores));

    [Fact(DisplayName = "BR-EV-004: the card is only ever given a quantiser, and the processor only a rate factor")]
    public void TheCardIsOnlyEverGivenAQuantiserAndTheProcessorOnlyARateFactor()
    {
        IReadOnlyList<string> onTheCard =
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores);
        IReadOnlyList<string> onTheProcessor =
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores);

        Assert.Contains("-rc_mode", onTheCard);
        Assert.Contains("CQP", onTheCard);
        Assert.Contains("-qp", onTheCard);
        Assert.DoesNotContain("-b:v", onTheCard);
        Assert.DoesNotContain("-maxrate", onTheCard);
        Assert.DoesNotContain("-bufsize", onTheCard);
        Assert.DoesNotContain("-crf", onTheCard);

        Assert.Contains("-crf", onTheProcessor);
        Assert.DoesNotContain("-qp", onTheProcessor);
        Assert.DoesNotContain("-b:v", onTheProcessor);
    }

    [Theory(DisplayName = "BR-EV-002: nothing that reaches an argument was written by anyone but this repository")]
    [MemberData(nameof(EveryShapeOnEveryEncoder))]
    public void EveryArgumentIsAnOptionNameAConstantOrThePathItWasHandedIn(
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        EncodeEncoder encoder)
    {
        string[] known =
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostats",
            "-progress",
            "pipe:1",
            "-y",
            "-filter_threads",
            "-threads",
            "2",
            "-vaapi_device",
            FfmpegEncodeInvocation.RenderNode,
            "-i",
            Source,
            "-map",
            "p:1040:v:0",
            "p:1040:a:0",
            "-vf",
            "-c:v",
            "libx264",
            "libx265",
            "h264_vaapi",
            "hevc_vaapi",
            "-preset",
            "medium",
            "-crf",
            "22",
            "-rc_mode",
            "CQP",
            "-qp",
            "24",
            "-c:a",
            "copy",
            "-bsf:a",
            "aac_adtstoasc",
            "bwdif=mode=send_frame,setsar=1",
            "bwdif=mode=send_field,setsar=1",
            "setsar=1",
            "bwdif=mode=send_frame,scale=1920:1080:flags=bicubic,setsar=1",
            "bwdif=mode=send_field,scale=1920:1080:flags=bicubic,setsar=1",
            "scale=1920:1080:flags=bicubic,setsar=1",
            "bwdif=mode=send_frame,scale=1280:720:flags=bicubic,setsar=1",
            "bwdif=mode=send_field,scale=1280:720:flags=bicubic,setsar=1",
            "scale=1280:720:flags=bicubic,setsar=1",
            "bwdif=mode=send_frame,setsar=1,format=nv12,hwupload",
            "bwdif=mode=send_field,setsar=1,format=nv12,hwupload",
            "setsar=1,format=nv12,hwupload",
            "bwdif=mode=send_frame,scale=1920:1080:flags=bicubic,setsar=1,format=nv12,hwupload",
            "bwdif=mode=send_field,scale=1920:1080:flags=bicubic,setsar=1,format=nv12,hwupload",
            "scale=1920:1080:flags=bicubic,setsar=1,format=nv12,hwupload",
            "bwdif=mode=send_frame,scale=1280:720:flags=bicubic,setsar=1,format=nv12,hwupload",
            "bwdif=mode=send_field,scale=1280:720:flags=bicubic,setsar=1,format=nv12,hwupload",
            "scale=1280:720:flags=bicubic,setsar=1,format=nv12,hwupload",
        ];

        IReadOnlyList<string> arguments = FfmpegEncodeInvocation.Arguments(
            Service,
            Profile(codec, resolution, deinterlace),
            encoder,
            Source, Cores);

        Assert.All(arguments, argument => Assert.Contains(argument, known, StringComparer.Ordinal));
    }

    [Theory(DisplayName = "BR-EV-002: an argument is never one piece of text carrying another")]
    [MemberData(nameof(EveryShapeOnEveryEncoder))]
    public void AnArgumentIsNeverOnePieceOfTextCarryingAnother(
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        EncodeEncoder encoder)
    {
        IReadOnlyList<string> arguments =
        [
            .. FfmpegEncodeInvocation.Arguments(Service, Profile(codec, resolution, deinterlace), encoder, Source, Cores),
            .. FfmpegEncodeInvocation.Delivery(Destination),
        ];

        string[] whatAShellWouldReadAgain = [" ", ";", "|", "&", "`", "$(", "\n"];

        Assert.All(
            arguments.Where(argument => argument != Source && argument != Destination),
            argument => Assert.DoesNotContain(
                whatAShellWouldReadAgain,
                mark => argument.Contains(mark, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheCardIsOnlyReachedForWhenItIsTheCardThatIsAsked()
    {
        Assert.DoesNotContain(
            "-vaapi_device",
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores));

        Assert.Contains(
            "-vaapi_device",
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores));
    }

    [Fact]
    public void AnInterlacedProfileSaysHowItIsUndoneAndOneLeftAloneSaysNothing()
    {
        Assert.Equal("bwdif=mode=send_field,setsar=1", FilterIn(Profile(deinterlace: Deinterlace.EveryField), EncodeEncoder.Software));
        Assert.Equal("setsar=1", FilterIn(Profile(deinterlace: Deinterlace.Leave), EncodeEncoder.Software));
    }

    [Fact]
    public void AProfileThatKeepsTheSourcesSizeAsksForNoScaling()
        => Assert.DoesNotContain(
            "scale",
            FilterIn(Profile(resolution: EncodeResolution.AsSource), EncodeEncoder.Software),
            StringComparison.Ordinal);

    [Fact]
    public void TheCodecPicksTheEncoderNameOnEitherSide()
    {
        Assert.Contains("libx265", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H265), EncodeEncoder.Software, Source, Cores));
        Assert.Contains("libx264", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H264), EncodeEncoder.Software, Source, Cores));
        Assert.Contains("hevc_vaapi", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H265), EncodeEncoder.Vaapi, Source, Cores));
        Assert.Contains("h264_vaapi", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H264), EncodeEncoder.Vaapi, Source, Cores));
    }

    private static string FilterIn(EncodeProfile profile, EncodeEncoder encoder)
    {
        IReadOnlyList<string> arguments = FfmpegEncodeInvocation.Arguments(Service, profile, encoder, Source, Cores);

        return arguments[arguments.ToList().IndexOf("-vf") + 1];
    }

    [Fact]
    public void WhatIsWrittenOutIsAskedForByTheFileItIsWrittenTo()
        => Assert.Equal(["-f", "mp4", "-movflags", "faststart", Destination], FfmpegEncodeInvocation.Delivery(Destination));

    [Fact]
    public void AnEncoderNobodyOffersIsNotAnEncoder()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegEncodeInvocation.Arguments(Service, Profile(), (EncodeEncoder)7, Source, Cores));

    [Fact(DisplayName = "BR-ED2-005: the core cap is handed to every stage that counts threads — the decoder, the filters and the encoder — and never as none")]
    public void TheCoreCapIsHandedToEveryStageThatCountsThreads()
    {
        string[] arguments = [.. FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, 3)];

        Assert.Equal(2, arguments.Count(argument => argument == "-threads"));
        Assert.Equal(1, arguments.Count(argument => argument == "-filter_threads"));
        Assert.All(
            arguments.Select((argument, at) => (argument, at)).Where(pair => pair.argument is "-threads" or "-filter_threads"),
            pair => Assert.Equal("3", arguments[pair.at + 1]));
        Assert.True(Array.IndexOf(arguments, "-threads") < Array.IndexOf(arguments, "-i"), "the decoder is told before the input is named");
        Assert.True(Array.LastIndexOf(arguments, "-threads") > Array.IndexOf(arguments, "-c:v"), "the encoder is told after it is named");
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, 0));
    }

    [Fact]
    public void ThereIsNothingToEncodeWithoutSomethingToReadFrom()
        => Assert.Throws<ArgumentException>(
            () => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, string.Empty, Cores));
}
