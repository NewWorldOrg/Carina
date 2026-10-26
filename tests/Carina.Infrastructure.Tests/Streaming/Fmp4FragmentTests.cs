using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class Fmp4FragmentTests
{
    private const uint OneFrame = 1001;

    private static readonly IReadOnlyDictionary<uint, Fmp4TrackDefaults> Tracks = new Dictionary<uint, Fmp4TrackDefaults>
    {
        [Fmp4Media.PictureTrack] = new(30_000, 0, 0, 0),
        [Fmp4Media.SoundTrack] = new(48_000, 1024, 0, Fmp4Media.SyncSample),
    };

    private static readonly uint[] TwentyFrames = [.. Enumerable.Repeat(OneFrame, 20)];

    [Fact]
    public void EveryTracksSamplesAndTheirBytesAreRead()
    {
        Fmp4Fragment read = Fmp4Fragment.Read(Fragment(), Tracks)!;
        Fmp4Run pictures = read.Of(Fmp4Media.PictureTrack)!;
        Fmp4Run sounds = read.Of(Fmp4Media.SoundTrack)!;

        Assert.Equal(20, pictures.Samples.Count);
        Assert.Equal(60UL * OneFrame, pictures.DecodeTime);
        Assert.Equal(20UL * OneFrame, pictures.Ticks);
        Assert.Equal(5, pictures.Samples.ToList().FindIndex(sample => sample.IsASync));
        Assert.Equal(Fmp4Media.PictureSample(5), pictures.Data[(5 * Fmp4Media.SampleLength)..(6 * Fmp4Media.SampleLength)].ToArray());
        Assert.Equal(31, sounds.Samples.Count);
        Assert.Equal(60UL * OneFrame * 48 / 30, sounds.DecodeTime);
        Assert.Equal(31 * Fmp4Media.SampleLength, sounds.Data.Length);
    }

    [Fact]
    public void AFragmentWrittenAgainReadsBackTheSame()
    {
        Fmp4Fragment read = Fmp4Fragment.Read(Fragment(), Tracks)!;
        Fmp4Fragment again = Fmp4Fragment.Read(read.Written(), Tracks)!;

        Assert.Equal(read.Runs.Count, again.Runs.Count);

        foreach ((Fmp4Run before, Fmp4Run after) in read.Runs.Zip(again.Runs))
        {
            Assert.Equal(before.Track, after.Track);
            Assert.Equal(before.DecodeTime, after.DecodeTime);
            Assert.Equal(before.Samples, after.Samples);
            Assert.Equal(before.Data.ToArray(), after.Data.ToArray());
        }
    }

    [Fact(DisplayName = "BR-PD-022: a fragment cut at a keyframe hands the pictures from the keyframe on to the second part, with the sound decoded beside them")]
    public void AFragmentCutAtAKeyframeHandsThePicturesFromItOnToTheSecondPart()
    {
        Fmp4Fragment read = Fmp4Fragment.Read(Fragment(), Tracks)!;

        (Fmp4Fragment before, Fmp4Fragment from) = read.CutAt(Fmp4Media.PictureTrack, 5, Tracks);
        Fmp4Fragment first = Fmp4Fragment.Read(before.Written(), Tracks)!;
        Fmp4Fragment second = Fmp4Fragment.Read(from.Written(), Tracks)!;

        Assert.Equal(5, first.Of(Fmp4Media.PictureTrack)!.Samples.Count);
        Assert.Equal(15, second.Of(Fmp4Media.PictureTrack)!.Samples.Count);
        Assert.True(second.Of(Fmp4Media.PictureTrack)!.Samples[0].IsASync);
        Assert.Equal(65UL * OneFrame, second.Of(Fmp4Media.PictureTrack)!.DecodeTime);
        Assert.Equal(Fmp4Media.PictureSample(5), second.Of(Fmp4Media.PictureTrack)!.Data[..Fmp4Media.SampleLength].ToArray());
        Assert.Equal(8, first.Of(Fmp4Media.SoundTrack)!.Samples.Count);
        Assert.Equal(23, second.Of(Fmp4Media.SoundTrack)!.Samples.Count);
        Assert.Equal((60UL * OneFrame * 48 / 30) + (8 * 1024), second.Of(Fmp4Media.SoundTrack)!.DecodeTime);
    }

    [Fact]
    public void WhatIsNotAMovieFragmentIsNotRead()
    {
        Assert.Null(Fmp4Fragment.Read(Fmp4.Joined(Fmp4.Box("free", 8), Fmp4.Box("mdat", 32)), Tracks));
        Assert.Null(Fmp4Fragment.Read(new byte[] { 0, 0, 0, 9, 1, 2 }, Tracks));
    }

    [Fact]
    public void AFragmentWhoseSamplesReachPastItsEndIsNotRead()
    {
        byte[] fragment = Fragment();

        Assert.Null(Fmp4Fragment.Read(fragment[..^Fmp4Media.SampleLength], Tracks));
    }

    [Fact]
    public void AFragmentOfATrackTheInitialisationNeverDeclaredIsNotRead()
    {
        Assert.Null(Fmp4Fragment.Read(
            Fragment(),
            new Dictionary<uint, Fmp4TrackDefaults> { [Fmp4Media.SoundTrack] = Tracks[Fmp4Media.SoundTrack] }));
    }

    private static byte[] Fragment()
        => Fmp4Media.Fragment(
            60UL * OneFrame,
            TwentyFrames,
            false,
            SampleFlagsCarried.EveryInTheRun,
            keyframeAt: 5,
            soundSamples: 31);
}
