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
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(LiveProfile.Hd30, Interlaced, LiveEncoder.Software);
        IReadOnlyList<string> playing = Arguments(TimeSpan.FromMinutes(1));

        Assert.Contains("nobuffer", live);
        Assert.Contains("low_delay", live);
        Assert.Contains("-copyts", live);

        Assert.DoesNotContain("nobuffer", playing);
        Assert.DoesNotContain("low_delay", playing);
        Assert.DoesNotContain("-copyts", playing);
        Assert.DoesNotContain("-fflags", playing);
        Assert.DoesNotContain("-flags", playing);
    }

    [Fact]
    public void TheTimelineOfWhatIsHandedBackStartsWhereTheViewerAskedRatherThanOnTheBroadcastClock()
    {
        Assert.DoesNotContain("-copyts", Arguments(TimeSpan.FromMinutes(10)));
        Assert.DoesNotContain("-start_at_zero", Arguments(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void WhatIsPlayedIsThePictureAndTheSoundAndNothingElseTheMultiplexHappenedToCarry()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero);

        Assert.Contains("-sn", arguments);
        Assert.Contains("-dn", arguments);
    }

    [Fact]
    public void ThePictureIsBuiltTheSameWayItIsBuiltForALiveViewer()
    {
        IReadOnlyList<string> live = FfmpegLiveInvocation.Arguments(LiveProfile.Hd30, Interlaced, LiveEncoder.Software);
        IReadOnlyList<string> playing = Arguments(TimeSpan.FromMinutes(1));

        Assert.Equal(After(live, "-vf"), After(playing, "-vf"));
        Assert.Equal(After(live, "-c:v"), After(playing, "-c:v"));
        Assert.Equal(After(live, "-b:v"), After(playing, "-b:v"));
    }

    [Fact]
    public void TheSoundIsCarriedOverRatherThanEncodedAgain()
    {
        IReadOnlyList<string> arguments = Arguments(TimeSpan.Zero);

        Assert.Equal("copy", After(arguments, "-c:a"));
        Assert.Equal("aac_adtstoasc", After(arguments, "-bsf:a"));
    }

    [Fact]
    public void ACardIsNamedBeforeTheInputWhenTheCardIsWhatEncodes()
    {
        IReadOnlyList<string> arguments = FfmpegPlaybackInvocation.Arguments(
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Vaapi,
            Recorded,
            TimeSpan.FromMinutes(1));

        Assert.Equal(FfmpegLiveInvocation.RenderNode, After(arguments, "-vaapi_device"));
        Assert.True(Where(arguments, "-vaapi_device") < Where(arguments, "-i"));
        Assert.Contains("h264_vaapi", arguments);
    }

    [Fact]
    public void WhatIsHandedBackIsTheSameFragmentedContainerALiveViewerIsHandedBack()
    {
        Assert.Equal(
            ["-f", "mp4", "-movflags", "empty_moov+default_base_moof", "-frag_duration", "200000", "pipe:1"],
            FfmpegLiveInvocation.Delivery());
    }

    [Fact]
    public void ARecordingIsPlayedFromSomewhereInItRatherThanFromBeforeItStarted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arguments(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentNullException>(() => FfmpegPlaybackInvocation.Arguments(
            null!,
            Interlaced,
            LiveEncoder.Software,
            Recorded,
            TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => FfmpegPlaybackInvocation.Arguments(
            LiveProfile.Hd30,
            Interlaced,
            LiveEncoder.Software,
            null!,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegPlaybackInvocation.Arguments(
            LiveProfile.Hd30,
            Interlaced,
            (LiveEncoder)99,
            Recorded,
            TimeSpan.Zero));
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
        => FfmpegPlaybackInvocation.Arguments(LiveProfile.Hd30, Interlaced, LiveEncoder.Software, Recorded, from);
}
