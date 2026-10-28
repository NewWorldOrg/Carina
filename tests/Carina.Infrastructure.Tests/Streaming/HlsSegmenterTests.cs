using System.Buffers.Binary;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class HlsSegmenterTests
{
    private const uint OneFrame = 1001;

    private const uint Timescale = 30_000;

    private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(1.5);

    private static readonly uint[] TwentyFrames = [.. Enumerable.Repeat(OneFrame, 20)];

    private static readonly IReadOnlyDictionary<uint, Fmp4TrackDefaults> Tracks = new Dictionary<uint, Fmp4TrackDefaults>
    {
        [Fmp4Media.PictureTrack] = new(Timescale, 0, 0, 0),
        [Fmp4Media.SoundTrack] = new(48_000, 1024, 0, Fmp4Media.SyncSample),
    };

    [Fact(DisplayName = "BR-PD-022: a segment begins at a keyframe and closes at the next keyframe once it has run long enough, as long as the pictures in it last")]
    public void ASegmentBeginsAtAKeyframeAndClosesAtTheNextOneOnceItHasRunLongEnough()
    {
        HlsSegmenter segmenter = Initialised();
        byte[] first = Gop(0, keyframe: true);
        byte[] second = Gop(1, keyframe: false);
        byte[] third = Gop(2, keyframe: false);

        segmenter.Take(Picture(first));
        segmenter.Take(Picture(second));
        segmenter.Take(Picture(third));

        Assert.Empty(segmenter.Window.Segments);
        Assert.False(segmenter.HoldsASegment);

        segmenter.Take(Picture(Gop(3, keyframe: true)));

        HlsSegment closed = Assert.Single(segmenter.Window.Segments);

        Assert.True(segmenter.HoldsASegment);

        Assert.Equal(0, closed.Number);
        Assert.Equal(Frames(60), closed.Length);
        Assert.Equal([.. first, .. second, .. third], segmenter.Segment(0)!.Value.ToArray());
        Assert.Equal(first.Length + second.Length + third.Length, closed.Bytes);
    }

    [Fact(DisplayName = "BR-PD-022: what comes before the first keyframe is left out, because a segment a player starts on begins with one")]
    public void WhatComesBeforeTheFirstKeyframeIsLeftOut()
    {
        HlsSegmenter segmenter = Initialised();
        byte[] keyframe = Gop(1, keyframe: true);

        segmenter.Take(Picture(Gop(0, keyframe: false)));
        segmenter.Take(Picture(keyframe));
        segmenter.Take(Picture(Gop(2, keyframe: false)));
        segmenter.Take(Picture(Gop(3, keyframe: false)));
        segmenter.Take(Picture(Gop(4, keyframe: true)));

        Assert.Equal(keyframe, segmenter.Segment(0)!.Value.ToArray()[..keyframe.Length]);
        Assert.Equal(Frames(60), segmenter.Window.Segments[0].Length);
    }

    [Fact(DisplayName = "BR-PD-022: a keyframe part way through a fragment closes the segment there, the fragment cut in two at it")]
    public void AKeyframePartWayThroughAFragmentClosesTheSegmentThere()
    {
        HlsSegmenter segmenter = Initialised();

        Whole(segmenter, 0);
        segmenter.Take(Picture(Fmp4Media.Fragment(Start(3), TwentyFrames, false, SampleFlagsCarried.EveryInTheRun, keyframeAt: 5)));
        segmenter.Take(Picture(Gop(4, keyframe: false)));
        segmenter.Take(Picture(Gop(5, keyframe: false)));
        segmenter.Take(Picture(Fmp4Media.Fragment(Start(6), TwentyFrames, false, SampleFlagsCarried.EveryInTheRun, keyframeAt: 5)));

        Assert.Equal([Frames(65), Frames(60)], segmenter.Window.Segments.Select(segment => segment.Length).ToArray());

        Fmp4Fragment begun = Fmp4Fragment.Read(segmenter.Segment(1)!.Value, Tracks)!;
        Fmp4Run pictures = begun.Of(Fmp4Media.PictureTrack)!;

        Assert.True(pictures.Samples[0].IsASync);
        Assert.Equal(Start(3) + (5 * OneFrame), pictures.DecodeTime);
        Assert.Equal(Fmp4Media.PictureSample(5), pictures.Data[..Fmp4Media.SampleLength].ToArray());
    }

    [Fact(DisplayName = "BR-PD-022: waiting for the first keyframe, one part way through a fragment begins the first segment there")]
    public void TheFirstKeyframePartWayThroughAFragmentBeginsTheFirstSegmentThere()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Fmp4Media.Fragment(0, TwentyFrames, false, SampleFlagsCarried.EveryInTheRun, keyframeAt: 15)));
        segmenter.Take(Picture(Gop(1, keyframe: false)));
        segmenter.Take(Picture(Gop(2, keyframe: false)));
        segmenter.Take(Picture(Gop(3, keyframe: false)));
        segmenter.Take(Picture(Gop(4, keyframe: true)));

        Assert.Equal(Frames(65), Assert.Single(segmenter.Window.Segments).Length);
        Assert.Equal(
            15UL * OneFrame,
            Fmp4Fragment.Read(segmenter.Segment(0)!.Value, Tracks)!.Of(Fmp4Media.PictureTrack)!.DecodeTime);
    }

    [Fact(DisplayName = "BR-PD-022: the sound decoded before the first keyframe is left out, so that the first segment begins with its picture")]
    public void TheSoundDecodedBeforeTheFirstKeyframeIsLeftOut()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Fmp4Media.Fragment(Start(3), TwentyFrames, true, soundSamples: 31, soundAhead: 3 * 1024)));
        segmenter.Take(Picture(Gop(4, keyframe: false)));
        segmenter.Take(Picture(Gop(5, keyframe: false)));
        segmenter.Take(Picture(Gop(6, keyframe: true)));

        Fmp4Run sounds = Fmp4Fragment.Read(segmenter.Segment(0)!.Value, Tracks)!.Of(Fmp4Media.SoundTrack)!;

        Assert.Equal(Start(3) * 48 / 30, sounds.DecodeTime);
        Assert.Equal(28, sounds.Samples.Count);
    }

    [Fact(DisplayName = "BR-PD-022: the sound decoded before a keyframe that begins a fragment stays with the segment it closes")]
    public void TheSoundDecodedBeforeAKeyframeBeginningAFragmentStaysWithTheSegmentItCloses()
    {
        HlsSegmenter segmenter = Initialised();

        Whole(segmenter, 0);
        segmenter.Take(Picture(Fmp4Media.Fragment(Start(3), TwentyFrames, true, soundSamples: 31, soundAhead: 2 * 1024)));
        segmenter.Take(Picture(Gop(4, keyframe: false)));
        segmenter.Take(Picture(Gop(5, keyframe: false)));
        Whole(segmenter, 2);

        Assert.Equal([Frames(60), Frames(60)], segmenter.Window.Segments.Select(segment => segment.Length).ToArray());
        Assert.Equal(8, SoundSamplesIn(segmenter.Segment(0)!.Value));
        Assert.Equal(29 + 4, SoundSamplesIn(segmenter.Segment(1)!.Value));
        Assert.Equal(
            Start(3) * 48 / 30,
            Fmp4Fragment.Read(segmenter.Segment(1)!.Value, Tracks)!.Of(Fmp4Media.SoundTrack)!.DecodeTime);
    }

    [Fact(DisplayName = "BR-PD-022: a keyframe that comes before the segment has run long enough does not close it")]
    public void AKeyframeThatComesTooSoonDoesNotCloseTheSegment()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Gop(0, keyframe: true)));
        segmenter.Take(Picture(Gop(1, keyframe: true)));
        segmenter.Take(Picture(Gop(2, keyframe: true)));

        Assert.Empty(segmenter.Window.Segments);

        segmenter.Take(Picture(Gop(3, keyframe: true)));

        Assert.Equal(Frames(60), Assert.Single(segmenter.Window.Segments).Length);
    }

    [Fact(DisplayName = "BR-PD-022: six segments are held, the oldest let go first, and the numbers go on from where they were")]
    public void SixSegmentsAreHeldAndTheOldestIsLetGoFirst()
    {
        HlsSegmenter segmenter = Initialised();

        for (int gop = 0; gop <= 8; gop++)
        {
            Whole(segmenter, gop);
        }

        Assert.Equal([2L, 3L, 4L, 5L, 6L, 7L], segmenter.Window.Segments.Select(segment => segment.Number).ToArray());
        Assert.Equal(2, segmenter.Window.FirstNumber);
        Assert.Null(segmenter.Segment(1));
        Assert.NotNull(segmenter.Segment(7));
        Assert.Null(segmenter.Segment(8));
    }

    [Fact(DisplayName = "BR-PD-022: a recording's playlist is an event that lists every segment from the first, however many are cut")]
    public void ARecordingsPlaylistListsEverySegmentFromTheFirst()
    {
        HlsSegmenter segmenter = Initialised(HlsPlaylistType.Event);

        for (int gop = 0; gop <= 8; gop++)
        {
            Whole(segmenter, gop);
        }

        Assert.Equal(HlsPlaylistType.Event, segmenter.Window.Type);
        Assert.Equal([0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L], segmenter.Window.Segments.Select(segment => segment.Number).ToArray());
        Assert.Equal(0, segmenter.Window.FirstNumber);
        Assert.NotNull(segmenter.Segment(0));
        Assert.Equal(8, segmenter.Cut);
    }

    [Fact(DisplayName = "BR-PD-022: a recording's segments a player has gone past are let go of, and its playlist goes on listing them")]
    public void ARecordingsSegmentsAPlayerHasGonePastAreLetGoOfAndStillListed()
    {
        HlsSegmenter segmenter = Initialised(HlsPlaylistType.Event);

        for (int gop = 0; gop <= 4; gop++)
        {
            Whole(segmenter, gop);
        }

        segmenter.ForgetBefore(2);

        Assert.Null(segmenter.Segment(0));
        Assert.Null(segmenter.Segment(1));
        Assert.NotNull(segmenter.Segment(2));
        Assert.NotNull(segmenter.Segment(3));
        Assert.Equal([0L, 1L, 2L, 3L], segmenter.Window.Segments.Select(segment => segment.Number).ToArray());
    }

    [Fact(DisplayName = "BR-PD-022: a recording transcoded to its end closes the segment left open, so that its last seconds are played")]
    public void ARecordingTranscodedToItsEndClosesTheSegmentLeftOpen()
    {
        HlsSegmenter segmenter = Initialised(HlsPlaylistType.Event);
        byte[] last = Gop(6, keyframe: true);

        Whole(segmenter, 0);
        Whole(segmenter, 1);
        segmenter.Take(Picture(last));
        segmenter.Finish();

        Assert.True(segmenter.Window.Ended);
        Assert.Equal([Frames(60), Frames(60), Frames(20)], segmenter.Window.Segments.Select(segment => segment.Length).ToArray());
        Assert.Equal(last, segmenter.Segment(2)!.Value.ToArray());
        Assert.Equal(3, segmenter.Cut);
    }

    [Fact]
    public void FinishingWithNothingLeftOpenOnlySaysTheEndCame()
    {
        HlsSegmenter segmenter = Initialised(HlsPlaylistType.Event);

        segmenter.Finish();

        Assert.True(segmenter.Window.Ended);
        Assert.Empty(segmenter.Window.Segments);
        Assert.False(segmenter.HoldsASegment);
    }

    [Fact]
    public void NothingTakenAfterTheEndIsCut()
    {
        HlsSegmenter segmenter = Initialised(HlsPlaylistType.Event);

        Whole(segmenter, 0);
        segmenter.Finish();
        Whole(segmenter, 1);
        Whole(segmenter, 2);

        Assert.Single(segmenter.Window.Segments);
    }

    [Fact]
    public void ALiveChannelsPlaylistSlides()
        => Assert.Equal(HlsPlaylistType.Sliding, Initialised().Window.Type);

    [Fact]
    public void APlaylistIsOneOfTheTypesNamedHere()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new HlsSegmenter(new HlsSettings(), (HlsPlaylistType)9));

    [Theory(DisplayName = "BR-PD-022: whether a fragment begins with a keyframe is read wherever the fragment or the track says it")]
    [InlineData(SampleFlagsCarried.FirstInTheRun)]
    [InlineData(SampleFlagsCarried.EveryInTheRun)]
    [InlineData(SampleFlagsCarried.DefaultOfTheFragment)]
    public void WhetherAFragmentBeginsWithAKeyframeIsReadWhereverItIsSaid(SampleFlagsCarried carried)
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Gop(0, keyframe: true, carried)));
        segmenter.Take(Picture(Gop(1, keyframe: false, carried)));
        segmenter.Take(Picture(Gop(2, keyframe: false, carried)));
        segmenter.Take(Picture(Gop(3, keyframe: false, carried)));

        Assert.Empty(segmenter.Window.Segments);

        segmenter.Take(Picture(Gop(4, keyframe: true, carried)));

        Assert.Single(segmenter.Window.Segments);
    }

    [Theory]
    [InlineData(Fmp4Media.SyncSample, 1)]
    [InlineData(Fmp4Media.NotASyncSample, 0)]
    public void AFragmentSayingNothingOfItsSamplesIsReadByTheDefaultOfItsTrack(uint byDefault, int closed)
    {
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(Fmp4Media.Initialisation(Timescale, trackDefaultFlags: byDefault)));

        for (int gop = 0; gop < 4; gop++)
        {
            segmenter.Take(Picture(Fmp4Media.Fragment(Start(gop), TwentyFrames, true, SampleFlagsCarried.DefaultOfTheTrack)));
        }

        Assert.Equal(closed, segmenter.Window.Segments.Count);
    }

    [Theory(DisplayName = "BR-PD-022: how long the pictures last is read wherever the fragment or the track says it")]
    [InlineData(SampleDurationsCarried.EveryInTheRun)]
    [InlineData(SampleDurationsCarried.DefaultOfTheFragment)]
    [InlineData(SampleDurationsCarried.DefaultOfTheTrack)]
    public void HowLongThePicturesLastIsReadWhereverItIsSaid(SampleDurationsCarried carried)
    {
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(Fmp4Media.Initialisation(Timescale, trackDefaultDuration: OneFrame)));

        for (int gop = 0; gop < 4; gop++)
        {
            segmenter.Take(Picture(Fmp4Media.Fragment(Start(gop), [.. Enumerable.Repeat(OneFrame, 60)], true, durations: carried)));
        }

        Assert.All(
            segmenter.Window.Segments,
            segment => Assert.Equal(Frames(60), segment.Length));
    }

    [Fact]
    public void AFragmentCarryingOnlySoundJoinsTheSegmentOpenAtTheTime()
    {
        HlsSegmenter segmenter = Initialised();
        byte[] keyframe = Gop(0, keyframe: true);
        byte[] sound = Fmp4Media.SoundAlone(Start(1));

        segmenter.Take(Picture(keyframe));
        segmenter.Take(Picture(sound));
        segmenter.Take(Picture(Gop(1, keyframe: false)));
        segmenter.Take(Picture(Gop(2, keyframe: false)));
        segmenter.Take(Picture(Gop(3, keyframe: true)));

        Assert.Equal([.. keyframe, .. sound], segmenter.Segment(0)!.Value.ToArray()[..(keyframe.Length + sound.Length)]);
    }

    [Fact]
    public void NothingIsCutBeforeTheInitialisationHasSaidWhichTrackIsThePicture()
    {
        HlsSegmenter segmenter = new(new HlsSettings());

        for (int gop = 0; gop < 6; gop++)
        {
            segmenter.Take(Picture(Gop(gop, keyframe: true)));
        }

        Assert.Empty(segmenter.Window.Segments);
        Assert.Null(segmenter.Initialisation);
    }

    [Fact]
    public void TheInitialisationIsHandedBackAsItCame()
    {
        byte[] initialisation = Fmp4Media.Initialisation(Timescale);
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(initialisation));

        Assert.Equal(initialisation, segmenter.Initialisation!.Value.ToArray());
    }

    [Theory(DisplayName = "BR-PD-022: the codecs are named from the initialisation, the picture by its profile and level and the sound as AAC")]
    [InlineData((byte)0x64, (byte)0x00, (byte)0x2A, true, "avc1.64002a,mp4a.40.2")]
    [InlineData((byte)0x4D, (byte)0x40, (byte)0x1F, true, "avc1.4d401f,mp4a.40.2")]
    [InlineData((byte)0x64, (byte)0x00, (byte)0x28, false, "avc1.640028")]
    public void TheCodecsAreNamedFromTheInitialisation(byte profile, byte compatibility, byte level, bool withSound, string named)
    {
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(Fmp4Media.Initialisation(Timescale, profile, compatibility, level, withSound)));

        Assert.Equal(named, segmenter.Window.Codecs);
    }

    [Fact]
    public void APictureInACodecAPlaylistCannotNameLeavesTheCodecsUnsaid()
    {
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(Fmp4Media.Initialisation(Timescale, pictureEntry: "hvc1")));

        Assert.Null(segmenter.Window.Codecs);
    }

    [Fact(DisplayName = "BR-PD-022: the target is the whole seconds the longest segment rounds to, and it never comes down")]
    public void TheTargetIsTheWholeSecondsTheLongestSegmentRoundsToAndNeverComesDown()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Fmp4Media.Fragment(0, [.. Enumerable.Repeat(OneFrame, 60)], true)));
        segmenter.Take(Picture(Fmp4Media.Fragment(60 * OneFrame, [.. Enumerable.Repeat(OneFrame, 100)], true)));

        Assert.Equal(2, segmenter.Window.TargetSeconds);

        segmenter.Take(Picture(Fmp4Media.Fragment(160 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true)));

        Assert.Equal(3, segmenter.Window.TargetSeconds);

        segmenter.Take(Picture(Fmp4Media.Fragment(220 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true)));

        Assert.Equal(3, segmenter.Window.TargetSeconds);
    }

    [Fact]
    public void AnInitialisationOtherThanTheFirstEndsTheCuttingAndTheSegmentLeftOpenIsDropped()
    {
        HlsSegmenter segmenter = Initialised();

        for (int gop = 0; gop < 5; gop++)
        {
            Whole(segmenter, gop);
        }

        segmenter.Take(Header(Fmp4Media.Initialisation(90_000)));
        Whole(segmenter, 5);
        Whole(segmenter, 6);

        Assert.True(segmenter.Window.Ended);
        Assert.Equal(4, segmenter.Window.Segments.Count);
    }

    [Fact]
    public void TheSameInitialisationHandedAgainChangesNothing()
    {
        byte[] initialisation = Fmp4Media.Initialisation(Timescale);
        HlsSegmenter segmenter = new(new HlsSettings());

        segmenter.Take(Header(initialisation));
        segmenter.Take(Header(initialisation));

        Assert.False(segmenter.Window.Ended);
    }

    [Fact]
    public void EndingDropsTheSegmentLeftOpenAndSaysSo()
    {
        HlsSegmenter segmenter = Initialised();

        Whole(segmenter, 0);
        segmenter.Take(Picture(Gop(3, keyframe: true)));
        segmenter.Take(Picture(Gop(4, keyframe: false)));
        segmenter.End();

        Assert.True(segmenter.Window.Ended);
        Assert.Single(segmenter.Window.Segments);
    }

    [Fact]
    public void WhatIsNotAPictureIsLeftAlone()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(new LiveFrame(LiveChannel.CaptionHeader, LivePts.Start, new byte[8]));
        segmenter.Take(new LiveFrame(LiveChannel.DataBroadcast, LivePts.Start, new byte[8]));

        Assert.Empty(segmenter.Window.Segments);
        Assert.False(segmenter.Window.Ended);
    }

    [Fact]
    public void AFragmentThatIsNotOneIsLeftOutRatherThanBreakingTheSegment()
    {
        HlsSegmenter segmenter = Initialised();

        segmenter.Take(Picture(Gop(0, keyframe: true)));
        segmenter.Take(Picture([0, 0, 0, 9, 1, 2]));
        segmenter.Take(Picture(Gop(1, keyframe: false)));
        segmenter.Take(Picture(Gop(2, keyframe: false)));
        segmenter.Take(Picture(Gop(3, keyframe: true)));

        Assert.Equal(Frames(60), Assert.Single(segmenter.Window.Segments).Length);
    }

    private static HlsSegmenter Initialised(HlsPlaylistType type = HlsPlaylistType.Sliding)
    {
        HlsSegmenter segmenter = new(new HlsSettings { ShortestSegment = Shortest }, type);

        segmenter.Take(Header(Fmp4Media.Initialisation(Timescale)));

        return segmenter;
    }

    private static int SoundSamplesIn(ReadOnlyMemory<byte> segment)
    {
        int counted = 0;
        int at = 0;

        while (at < segment.Length)
        {
            int moof = (int)BinaryPrimitives.ReadUInt32BigEndian(segment.Span[at..]);
            int mdat = (int)BinaryPrimitives.ReadUInt32BigEndian(segment.Span[(at + moof)..]);

            counted += Fmp4Fragment.Read(segment.Slice(at, moof + mdat), Tracks)!.Of(Fmp4Media.SoundTrack)?.Samples.Count ?? 0;
            at += moof + mdat;
        }

        return counted;
    }

    private static void Whole(HlsSegmenter segmenter, int gop)
    {
        segmenter.Take(Picture(Gop(gop * 3, keyframe: true)));
        segmenter.Take(Picture(Gop((gop * 3) + 1, keyframe: false)));
        segmenter.Take(Picture(Gop((gop * 3) + 2, keyframe: false)));
    }

    private static TimeSpan Frames(long count) => TimeSpan.FromTicks(count * OneFrame * TimeSpan.TicksPerSecond / Timescale);

    private static ulong Start(int gop) => (ulong)gop * 20 * OneFrame;

    private static byte[] Gop(int third, bool keyframe, SampleFlagsCarried carried = SampleFlagsCarried.FirstInTheRun)
        => Fmp4Media.Fragment(Start(third), TwentyFrames, keyframe, carried);

    private static LiveFrame Header(byte[] bytes) => new(LiveChannel.PictureHeader, LivePts.Start, bytes);

    private static LiveFrame Picture(byte[] bytes) => new(LiveChannel.Picture, LivePts.Start, bytes);
}
