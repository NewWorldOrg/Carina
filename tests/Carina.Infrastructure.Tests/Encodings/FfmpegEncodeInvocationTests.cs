using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegEncodeInvocationTests
{
    private const string Source = "/srv/recordings/0f8c.ts";

    private const string Destination = "/srv/encoded/0f8c.mp4";

    private static readonly ServiceId Service = new(1040);

    private static readonly EncodeSound AsItStands = EncodeSound.EveryStreamAsItStands;

    private static readonly EncodeSound TwoLanguagesOnOneSound = EncodeSound.Of(AudioMode.DualMono, 1);

    private const int Cores = 2;

    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5072);

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

    [Fact(DisplayName = "BR-PD-008: a broadcast that put two languages on one sound is encoded with the main language in both ears, not with both languages side by side")]
    public void TheArgumentsForTwoLanguagesOnOneSoundAreExactlyThese()
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
                "-ss",
                "0.5072",
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
                "-af",
                "pan=stereo|c0=c0|c1=c0",
                "-c:a",
                "aac",
                "-b:a",
                "192k",
            ],
            FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(),
                EncodeEncoder.Software,
                Source,
                Cores,
                HeadSkip,
                TwoLanguagesOnOneSound));

    [Fact(DisplayName = "BR-PD-008: the card puts the main language in both ears exactly as the processor does, because the real machine encodes on the card")]
    public void TheArgumentsForTwoLanguagesOnOneSoundOnTheCardAreExactlyThese()
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
                "-ss",
                "0.5072",
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
                "-af",
                "pan=stereo|c0=c0|c1=c0",
                "-c:a",
                "aac",
                "-b:a",
                "192k",
            ],
            FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(),
                EncodeEncoder.Vaapi,
                Source,
                Cores,
                HeadSkip,
                TwoLanguagesOnOneSound));

    [Fact]
    public void TheMainLanguageIsTakenFromTheSameChannelPlaybackTakesItFrom()
        => Assert.Contains(
            FfmpegPlaybackInvocation.TheLeftChannelInBothEars,
            FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(),
                EncodeEncoder.Software,
                Source,
                Cores,
                HeadSkip,
                TwoLanguagesOnOneSound));

    [Theory(DisplayName = "BR-PD-008: a sound that stands on a stream of its own is copied over exactly as it was before any language was chosen")]
    [InlineData(AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced)]
    [InlineData(AudioMode.Mono, 1)]
    [InlineData(AudioMode.Stereo, 1)]
    [InlineData(AudioMode.Surround, 1)]
    [InlineData(AudioMode.DualMono, 2)]
    [InlineData(AudioMode.Stereo, 2)]
    public void ASoundOnAStreamOfItsOwnIsCopiedOverExactlyAsItWas(AudioMode audio, int sounds)
        => Assert.Equal(
            FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(),
                EncodeEncoder.Software,
                Source,
                Cores,
                HeadSkip,
                AsItStands),
            FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(),
                EncodeEncoder.Software,
                Source,
                Cores,
                HeadSkip,
                EncodeSound.Of(audio, sounds)));

    [Fact]
    public void ARunIsBuiltForOneSoundOrForNone()
        => Assert.Throws<ArgumentNullException>(() => FfmpegEncodeInvocation.Arguments(
            Service,
            Profile(),
            EncodeEncoder.Software,
            Source,
            Cores,
            HeadSkip,
            null!));

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
                "-ss",
                "0.5072",
                "-map",
                "p:1040:v:0",
                "-map",
                "p:1040:a",
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
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands));

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
                "-ss",
                "0.5072",
                "-map",
                "p:1040:v:0",
                "-map",
                "p:1040:a",
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
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores, HeadSkip, AsItStands));

    [Fact(DisplayName = "BR-EV-004: the card is only ever given a quantiser, and the processor only a rate factor")]
    public void TheCardIsOnlyEverGivenAQuantiserAndTheProcessorOnlyARateFactor()
    {
        IReadOnlyList<string> onTheCard =
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores, HeadSkip, AsItStands);
        IReadOnlyList<string> onTheProcessor =
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands);

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
            "-ss",
            "0.5072",
            "-map",
            "p:1040:v:0",
            "p:1040:a",
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
            "-af",
            FfmpegPlaybackInvocation.TheLeftChannelInBothEars,
            "aac",
            "-b:a",
            "192k",
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

        IReadOnlyList<string> arguments =
        [
            .. FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(codec, resolution, deinterlace),
                encoder,
                Source,
                Cores,
                HeadSkip,
                AsItStands),
            .. FfmpegEncodeInvocation.Arguments(
                Service,
                Profile(codec, resolution, deinterlace),
                encoder,
                Source,
                Cores,
                HeadSkip,
                TwoLanguagesOnOneSound),
        ];

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
            .. FfmpegEncodeInvocation.Arguments(Service, Profile(codec, resolution, deinterlace), encoder, Source, Cores, HeadSkip, AsItStands),
            .. FfmpegEncodeInvocation.Arguments(Service, Profile(codec, resolution, deinterlace), encoder, Source, Cores, HeadSkip, TwoLanguagesOnOneSound),
            .. FfmpegEncodeInvocation.Delivery(Destination),
        ];

        string[] whatAShellWouldReadAgain = [" ", ";", "|", "&", "`", "$(", "\n"];
        string[] writtenHereAndReadByNoShell =
        [
            Source,
            Destination,
            FfmpegPlaybackInvocation.TheLeftChannelInBothEars,
        ];

        Assert.All(
            arguments.Where(argument => !writtenHereAndReadByNoShell.Contains(argument, StringComparer.Ordinal)),
            argument => Assert.DoesNotContain(
                whatAShellWouldReadAgain,
                mark => argument.Contains(mark, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheCardIsOnlyReachedForWhenItIsTheCardThatIsAsked()
    {
        Assert.DoesNotContain(
            "-vaapi_device",
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands));

        Assert.Contains(
            "-vaapi_device",
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores, HeadSkip, AsItStands));
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
        Assert.Contains("libx265", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H265), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands));
        Assert.Contains("libx264", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H264), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands));
        Assert.Contains("hevc_vaapi", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H265), EncodeEncoder.Vaapi, Source, Cores, HeadSkip, AsItStands));
        Assert.Contains("h264_vaapi", FfmpegEncodeInvocation.Arguments(Service, Profile(EncodeCodec.H264), EncodeEncoder.Vaapi, Source, Cores, HeadSkip, AsItStands));
    }

    private static string FilterIn(EncodeProfile profile, EncodeEncoder encoder)
    {
        IReadOnlyList<string> arguments = FfmpegEncodeInvocation.Arguments(Service, profile, encoder, Source, Cores, HeadSkip, AsItStands);

        return arguments[arguments.ToList().IndexOf("-vf") + 1];
    }

    [Fact]
    public void WhatIsWrittenOutIsAskedForByTheFileItIsWrittenTo()
        => Assert.Equal(["-f", "mp4", "-movflags", "faststart", Destination], FfmpegEncodeInvocation.Delivery(Destination));

    [Fact]
    public void AnEncoderNobodyOffersIsNotAnEncoder()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegEncodeInvocation.Arguments(Service, Profile(), (EncodeEncoder)7, Source, Cores, HeadSkip, AsItStands));

    [Fact(DisplayName = "BR-ED2-005: the core cap is handed to every stage that counts threads — the decoder, the filters and the encoder — and never as none")]
    public void TheCoreCapIsHandedToEveryStageThatCountsThreads()
    {
        string[] arguments = [.. FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, 3, HeadSkip, AsItStands)];

        Assert.Equal(2, arguments.Count(argument => argument == "-threads"));
        Assert.Equal(1, arguments.Count(argument => argument == "-filter_threads"));
        Assert.All(
            arguments.Select((argument, at) => (argument, at)).Where(pair => pair.argument is "-threads" or "-filter_threads"),
            pair => Assert.Equal("3", arguments[pair.at + 1]));
        Assert.True(Array.IndexOf(arguments, "-threads") < Array.IndexOf(arguments, "-i"), "the decoder is told before the input is named");
        Assert.True(Array.LastIndexOf(arguments, "-threads") > Array.IndexOf(arguments, "-c:v"), "the encoder is told after it is named");
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, 0, HeadSkip, AsItStands));
    }

    [Fact]
    public void ThereIsNothingToEncodeWithoutSomethingToReadFrom()
        => Assert.Throws<ArgumentException>(
            () => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, string.Empty, Cores, HeadSkip, AsItStands));

    [Fact(DisplayName = "BR-ED2-006: the head skip is the one -ss, it stands after the input as a trim and not before it as a seek, it is written to the microsecond, and neither -output_ts_offset nor -copyts is anywhere near it")]
    public void TheHeadSkipIsTheOneSsAndItStandsAfterTheInput()
    {
        string[] arguments = [.. FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(0.507200), AsItStands)];
        string[] onTheCard = [.. FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Vaapi, Source, Cores, TimeSpan.Zero, AsItStands)];

        int input = Array.IndexOf(arguments, "-i");
        int skip = Array.IndexOf(arguments, "-ss");

        Assert.Equal(1, arguments.Count(argument => argument == "-ss"));
        Assert.True(skip > input, "the skip is a trim after the input, not a seek before it");
        Assert.True(skip < Array.IndexOf(arguments, "-map"), "the skip is written before anything is mapped");
        Assert.Equal("0.5072", arguments[skip + 1]);
        Assert.Equal("0", onTheCard[Array.IndexOf(onTheCard, "-ss") + 1]);
        Assert.DoesNotContain("-output_ts_offset", arguments);
        Assert.DoesNotContain("-copyts", arguments);
        Assert.DoesNotContain("-start_at_zero", arguments);
        Assert.DoesNotContain("-avoid_negative_ts", arguments);
        Assert.Equal(
            "1.000001",
            FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(1.000001), AsItStands)
                .SkipWhile(argument => argument != "-ss").Skip(1).First());
    }

    [Fact(DisplayName = "BR-ED2-006: a head skip beyond the five seconds a run accepts, or before nothing, is refused before a run is built — a broadcast clock handed in as a skip is the seventeen hours")]
    public void AHeadSkipBeyondReachIsRefusedBeforeARunIsBuilt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(5.5), AsItStands));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(62170), AsItStands));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(-0.1), AsItStands));
        _ = FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, TimeSpan.FromSeconds(5), AsItStands);
    }

    [Fact(DisplayName = "BR-ED2-006: every audio stream of the programme is mapped and copied, not the first alone")]
    public void EveryAudioStreamOfTheProgrammeIsMappedAndCopied()
    {
        string[] arguments = [.. FfmpegEncodeInvocation.Arguments(Service, Profile(), EncodeEncoder.Software, Source, Cores, HeadSkip, AsItStands)];

        Assert.Contains("p:1040:a", arguments);
        Assert.DoesNotContain("p:1040:a:0", arguments);
        Assert.Equal("copy", arguments[Array.IndexOf(arguments, "-c:a") + 1]);
    }
}
