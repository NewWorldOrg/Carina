using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class ChannelDifferenceTests
{
    private const int TwoSeconds = 2 * SoundReader.SampleRate;

    [Fact(DisplayName = "silence reads no likeness and no difference")]
    public void SilenceReadsNothing()
    {
        SoundReadings readings = SyntheticSound.Read(new short[2 * TwoSeconds]);

        Assert.All(readings.Channels, reading => Assert.Equal(new ChannelDifference(ChannelDifference.Unmeasured, SoundLoudness.Silent), reading));
    }

    [Fact(DisplayName = "the same sound in both channels reads wholly alike and with no difference")]
    public void MonoReadsWhollyAlike()
    {
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(21, TwoSeconds)));

        Assert.All(readings.Channels, reading =>
        {
            Assert.Equal(1.0, reading.Likeness);
            Assert.Equal(SoundLoudness.Silent, reading.Difference);
        });
    }

    [Fact(DisplayName = "a stereo mix reads partly alike, with a difference quieter than the sound")]
    public void StereoReadsPartlyAlike()
    {
        short[] left = SyntheticSound.Tones(21, TwoSeconds);
        short[] right = SyntheticSound.Mixed(left, 0.8, SyntheticSound.Tones(22, TwoSeconds));
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Interleaved(left, right));

        Assert.All(readings.Channels.Zip(MeanLoudness(readings)), pair =>
        {
            Assert.InRange(pair.First.Likeness ?? double.NaN, 0.3, 0.99);
            Assert.True(pair.First.Difference > pair.Second + 12);
        });
    }

    [Fact(DisplayName = "two unrelated sounds, one in each channel, read unalike, with a difference about as loud as the sound")]
    public void TwoLanguagesReadUnalike()
    {
        short[] left = SyntheticSound.Tones(21, TwoSeconds);
        short[] right = SyntheticSound.Tones(22, TwoSeconds);
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Interleaved(left, right));

        Assert.All(readings.Channels.Zip(MeanLoudness(readings)), pair =>
        {
            Assert.InRange(pair.First.Likeness ?? double.NaN, -0.3, 0.3);
            Assert.InRange(pair.First.Difference, pair.Second + 2, pair.Second + 10);
        });
    }

    [Fact(DisplayName = "one channel silent while the other sounds reads no likeness but a difference")]
    public void OneSilentChannelReadsNoLikeness()
    {
        short[] left = SyntheticSound.Tones(21, TwoSeconds);
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Interleaved(left, new short[TwoSeconds]));

        Assert.All(readings.Channels, reading =>
        {
            Assert.Null(reading.Likeness);
            Assert.True(reading.Difference < SoundLoudness.Silent);
        });
    }

    [Fact(DisplayName = "a channel difference is written for every 100 ms, the last one for what is left when the sound is finished")]
    public void AChannelDifferenceIsWrittenEveryHundredMilliseconds()
    {
        short[] sound = SyntheticSound.Mono(SyntheticSound.Tones(21, (2 * ChannelDifference.Hop) + 1));
        SoundReader reader = new();
        SoundReadings readings = new();

        reader.Hear(sound, readings);
        Assert.Equal(2, readings.Channels.Count);

        reader.Finish(readings);
        Assert.Equal(3, readings.Channels.Count);
        Assert.Equal(100, ChannelDifference.Hop * 1000 / SoundReader.SampleRate);
    }

    private static IEnumerable<int> MeanLoudness(SoundReadings readings)
        => readings.Loudness
            .Chunk(ChannelDifference.Hop / SoundLoudness.Hop)
            .Select(loudness => loudness.Average(step => Math.Pow(10, -SoundLoudness.DecibelsBelowFullScale(step) / 10)))
            .Select(power => (int)Math.Round(-10 * Math.Log10(power) / SoundLoudness.StepDecibels));
}
