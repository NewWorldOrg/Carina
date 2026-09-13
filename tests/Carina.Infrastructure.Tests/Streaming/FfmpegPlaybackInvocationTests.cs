using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfmpegPlaybackInvocationTests
{
    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private static readonly StreamSource Recorded = new("/srv/recordings/a1b2c3.ts");

    private static readonly ServiceId Service = new(1040);

    private static readonly SoundPlacement TheWholeFirstStream = SoundPlacement.WholeStream(0);

    private static readonly SoundPlacement TheWholeSecondStream = SoundPlacement.WholeStream(1);

    private static readonly SoundPlacement TheLeftOfTheFirstStream =
        SoundPlacement.OneChannelOf(0, SoundChannel.Left);

    private static readonly SoundPlacement TheRightOfTheFirstStream =
        SoundPlacement.OneChannelOf(0, SoundChannel.Right);

    [Fact]
    public void TheStartingPositionIsGivenBeforeTheInputSoTheSeekHappensBeforeAnythingIsRead()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.FromMinutes(10));

        int position = Where(arguments, "-ss");
        int input = Where(arguments, "-i");

        Assert.True(position >= 0);
        Assert.True(input > position);
        Assert.Equal("600", arguments[position + 1]);
        Assert.Equal(Recorded.Value, arguments[input + 1]);
    }

    [Fact]
    public void APositionBetweenTwoSecondsIsWrittenOutInSecondsAndNotInWhateverTheMachineCallsThem()
    {
        Assert.Equal("90.5", After(Arguments(TimeSpan.FromSeconds(90.5)), "-ss"));
        Assert.Equal("0", After(Arguments(TimeSpan.Zero), "-ss"));
    }

    [Fact]
    public void NothingAskedForBecauseTheLiveInputCannotBeRewoundIsAskedForOfAFileThatCan()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, CaptionOutlet.None);
        IReadOnlyList<string> playing = Arguments(TimeSpan.FromMinutes(1));

        Assert.Contains("nobuffer", live);
        Assert.Contains("-copyts", live);

        Assert.DoesNotContain("nobuffer", playing);
        Assert.DoesNotContain("-copyts", playing);
        Assert.DoesNotContain("-fflags", playing);
    }

    [Fact]
    public void TheTimelineOfWhatIsHandedBackStartsWhereTheViewerAskedRatherThanOnTheBroadcastClock()
    {
        Assert.DoesNotContain("-copyts", Arguments(TimeSpan.FromMinutes(10)));
        Assert.DoesNotContain("-start_at_zero", Arguments(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void WhatIsPlayedIsTheRecordedServicesPictureAndItsMainSoundAndNothingElseTheMultiplexCarried()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero);

        Assert.Equal(["p:1040:v:0", "p:1040:a:0"], Mapped(arguments));
        Assert.True(Where(arguments, "-map") > Where(arguments, "-i"));
    }

    [Fact]
    public void AViewerWhoNamesNoSoundIsGivenTheOneTheyAlwaysWere()
    {
        Assert.Equal(Arguments(TimeSpan.Zero), Arguments(TimeSpan.Zero, TheWholeFirstStream));
    }

    [Fact]
    public void TheSecondSoundOfTheRecordedServiceIsTakenWhenItIsAskedFor()
    {
        Assert.Equal(["p:1040:v:0", "p:1040:a:1"], Mapped(Arguments(TimeSpan.Zero, TheWholeSecondStream)));
    }

    [Fact]
    public void StillOnlyOneSoundIsBuiltWhenTheSecondOneIsAskedFor()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.FromMinutes(1), TheWholeSecondStream);

        Assert.Equal("aac", After(arguments, "-c:a"));
        Assert.Single(arguments, argument => string.Equals(argument, "-c:a", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("-c:a:", StringComparison.Ordinal));
        Assert.Single(Mapped(arguments), map => map.StartsWith("p:1040:a", StringComparison.Ordinal));
    }

    [Fact]
    public void APlayedRecordingIsGivenTheSameSecondSoundALiveViewerIs()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(
            Service,
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Software,
            CaptionOutlet.None,
            SoundTrack.Secondary);

        Assert.Equal(Mapped(live), Mapped(Arguments(TimeSpan.FromMinutes(1), TheWholeSecondStream)));
    }

    [Fact]
    public void ASoundTakenFromNowhereIsRefusedBeforeAnythingIsBuilt()
    {
        Assert.Throws<ArgumentNullException>(() => Arguments(TimeSpan.Zero, null!));
    }

    [Fact]
    public void TheWholeOfAStreamIsBuiltWordForWordTheWayItWasBeforeAnySoundCouldBeSplit()
    {
        Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-ss",
                "60",
                "-i",
                "/srv/recordings/a1b2c3.ts",
                "-map",
                "p:1040:v:0",
                "-map",
                "p:1040:a:0",
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
                "aac",
                "-b:a",
                "192k",
            ],
            Arguments(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void TheMainSoundOfABroadcastThatPutTwoLanguagesOnOneStreamIsItsLeftChannelInBothEars()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero, TheLeftOfTheFirstStream);

        Assert.Equal(["p:1040:v:0", "p:1040:a:0"], Mapped(arguments));
        Assert.Equal("pan=stereo|c0=c0|c1=c0", After(arguments, "-af"));
        Assert.Single(arguments, argument => string.Equals(argument, "-af", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSecondarySoundOfABroadcastThatPutTwoLanguagesOnOneStreamIsItsRightChannelInBothEars()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero, TheRightOfTheFirstStream);

        Assert.Equal(["p:1040:v:0", "p:1040:a:0"], Mapped(arguments));
        Assert.Equal("pan=stereo|c0=c1|c1=c1", After(arguments, "-af"));
    }

    [Fact]
    public void OneChannelTakenOutOfAStreamIsHandedBackOnBothOfThemRatherThanOnTheSideItCameFrom()
    {
        Assert.Equal(FfmpegPlaybackInvocation.TheLeftChannelInBothEars, "pan=stereo|c0=c0|c1=c0");
        Assert.Equal(FfmpegPlaybackInvocation.TheRightChannelInBothEars, "pan=stereo|c0=c1|c1=c1");
    }

    [Fact]
    public void ChoosingOneChannelOfAStreamChangesNothingElseInTheCommand()
    {
        IReadOnlyList<string> whole = Arguments(TimeSpan.FromMinutes(1));
        IReadOnlyList<string> half = Arguments(TimeSpan.FromMinutes(1), TheLeftOfTheFirstStream);

        Assert.Equal([.. whole, "-af", FfmpegPlaybackInvocation.TheLeftChannelInBothEars], half);
    }

    [Fact]
    public void ALiveViewerIsGivenTheSoundBuiltTheSameWayAPlayedRecordingIs()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, CaptionOutlet.None);
        IReadOnlyList<string> playing = Arguments(TimeSpan.Zero);

        Assert.Equal("aac", After(live, "-c:a"));
        Assert.Equal(After(playing, "-c:a"), After(live, "-c:a"));
        Assert.Equal(After(playing, "-b:a"), After(live, "-b:a"));
        Assert.DoesNotContain("-bsf:a", live);
    }

    [Fact]
    public void APlayedRecordingCarriesTheSameOneSoundALiveViewerIsGiven()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, CaptionOutlet.None);
        IReadOnlyList<string> playing = Arguments(TimeSpan.FromMinutes(1));

        Assert.Equal(Mapped(live), Mapped(playing));
        Assert.DoesNotContain("p:1040:a", Mapped(playing));
    }

    [Fact]
    public void ThePictureIsBuiltTheSameWayItIsBuiltForALiveViewer()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software, CaptionOutlet.None);
        IReadOnlyList<string> playing = Arguments(TimeSpan.FromMinutes(1));

        Assert.Equal(After(live, "-vf"), After(playing, "-vf"));
        Assert.Equal(After(live, "-c:v"), After(playing, "-c:v"));
        Assert.Equal(After(live, "-b:v"), After(playing, "-b:v"));
    }

    [Fact]
    public void TheSoundIsEncodedAgainSoThatADamagedFrameInTheRecordingNeverReachesTheBrowser()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero);

        Assert.Equal("aac", After(arguments, "-c:a"));
        Assert.Equal("192k", After(arguments, "-b:a"));
        Assert.DoesNotContain("copy", arguments);
    }

    [Fact]
    public void TheSoundIsGivenBackAtTheRateItWasBroadcastAtRatherThanAtWhateverTextWasHandedIn()
    {
        Assert.Equal(192, FfmpegLiveInvocation.SoundKilobitsPerSecond);
        Assert.Equal(
            FormattableString.Invariant($"{FfmpegLiveInvocation.SoundKilobitsPerSecond}k"),
            After(Arguments(TimeSpan.Zero), "-b:a"));
    }

    [Fact]
    public void NothingReshapesASoundTakenWholeSoASurroundRecordingKeepsItsChannels()
    {
        foreach (IReadOnlyList<string> arguments in new[]
        {
            Arguments(TimeSpan.Zero),
            Arguments(TimeSpan.Zero, TheWholeSecondStream),
        })
        {
            Assert.DoesNotContain("-bsf:a", arguments);
            Assert.DoesNotContain("-ac", arguments);
            Assert.DoesNotContain("-ar", arguments);
            Assert.DoesNotContain("-af", arguments);
            Assert.DoesNotContain("-filter:a", arguments);
            Assert.DoesNotContain("-channel_layout", arguments);
        }
    }

    [Fact]
    public void ACardIsNamedBeforeTheInputWhenTheCardIsWhatEncodes()
    {
        IReadOnlyList<string> arguments = FfmpegPlaybackInvocation.Arguments(
            Service,
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Vaapi,
            Recorded,
            TimeSpan.FromMinutes(1),
            TheWholeFirstStream);

        Assert.Equal(FfmpegLiveInvocation.RenderNode, After(arguments, "-vaapi_device"));
        Assert.True(Where(arguments, "-vaapi_device") < Where(arguments, "-i"));
        Assert.Contains("h264_vaapi", arguments);
    }

    [Fact]
    public void WhatIsHandedBackIsTheSameFragmentedContainerALiveViewerIsHandedBackWithItsClockStartedAfresh()
    {
        Assert.Equal(
            ["-f", "mp4", "-movflags", "empty_moov+default_base_moof+delay_moov", "-frag_duration", "200000", "pipe:1"],
            FfmpegLiveInvocation.DeliveryFromTheStart());
    }

    [Fact]
    public void ARecordingIsPlayedFromSomewhereInItRatherThanFromBeforeItStarted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arguments(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentNullException>(() => FfmpegPlaybackInvocation.Arguments(
            null!,
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Software,
            Recorded,
            TimeSpan.Zero,
            TheWholeFirstStream));
        Assert.Throws<ArgumentNullException>(() => FfmpegPlaybackInvocation.Arguments(
            Service,
            null!,
            Interlaced,
            LiveEncoder.Software,
            Recorded,
            TimeSpan.Zero,
            TheWholeFirstStream));
        Assert.Throws<ArgumentNullException>(() => FfmpegPlaybackInvocation.Arguments(
            Service,
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Software,
            null!,
            TimeSpan.Zero,
            TheWholeFirstStream));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegPlaybackInvocation.Arguments(
            Service,
            LiveProfile.Hd30,
            Interlaced,
            (LiveEncoder)99,
            Recorded,
            TimeSpan.Zero,
            TheWholeFirstStream));
    }

    private static int Where(IReadOnlyList<string> arguments, string option)
    {
        for (int at = 0; at < arguments.Count; at++)
        {
            if (string.Equals(arguments[at], option, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }

    private static string After(IReadOnlyList<string> arguments, string option)
    {
        int at = Where(arguments, option);

        Assert.True(at >= 0, $"nothing in the command names {option}");

        return arguments[at + 1];
    }

    private static IReadOnlyList<string> Arguments(TimeSpan from)
        => Arguments(from, TheWholeFirstStream);

    private static IReadOnlyList<string> Arguments(TimeSpan from, SoundPlacement sound)
        => FfmpegPlaybackInvocation.Arguments(
            Service,
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Software,
            Recorded,
            from,
            sound);

    private static string[] Mapped(IReadOnlyList<string> arguments)
        =>
        [
            .. arguments
                .Select((argument, at) => (argument, at))
                .Where(pair => string.Equals(pair.argument, "-map", StringComparison.Ordinal))
                .Select(pair => arguments[pair.at + 1]),
        ];
}
