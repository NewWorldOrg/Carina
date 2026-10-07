using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SoundLoudnessTests
{
    private const int OneSecond = SoundReader.SampleRate;

    [Fact(DisplayName = "a tone's loudness is how far its power lies below full scale, in half-decibel steps")]
    public void AToneIsMeasuredBelowFullScale()
    {
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Sine(1000, 3276.8, OneSecond)));

        Assert.All(readings.Loudness, loudness => Assert.Equal(46, loudness));
        Assert.Equal(23, SoundLoudness.DecibelsBelowFullScale(46));
    }

    [Fact(DisplayName = "a tone at half the amplitude reads twelve steps quieter")]
    public void HalfTheAmplitudeIsTwelveStepsQuieter()
    {
        SoundReadings full = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Sine(1000, 3276.8, OneSecond)));
        SoundReadings half = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Sine(1000, 1638.4, OneSecond)));

        Assert.All(full.Loudness.Zip(half.Loudness), pair => Assert.Equal(12, pair.Second - pair.First));
    }

    [Fact(DisplayName = "digital silence reads silent, and the quietest sound that is not silence reads above it")]
    public void SilenceReadsSilent()
    {
        short[] whisper = new short[2 * OneSecond];
        Array.Fill<short>(whisper, 1);

        Assert.All(SyntheticSound.Read(new short[2 * OneSecond]).Loudness, loudness => Assert.Equal(SoundLoudness.Silent, loudness));
        Assert.All(SyntheticSound.Read(whisper).Loudness, loudness => Assert.InRange(loudness, 150, 200));
        Assert.Equal(0, SoundLoudness.Of(32768.0 * 32768.0));
        Assert.Equal(SoundLoudness.Silent, SoundLoudness.Of(1e-30));
    }

    [Fact(DisplayName = "a loudness is written for every 20 ms, the last one for what is left when the sound is finished")]
    public void ALoudnessIsWrittenEveryTwentyMilliseconds()
    {
        short[] sound = SyntheticSound.Mono(SyntheticSound.Sine(1000, 3276.8, (3 * SoundLoudness.Hop) + 40));
        SoundReader reader = new();
        SoundReadings readings = new();

        reader.Hear(sound, readings);
        Assert.Equal(3, readings.Loudness.Count);

        reader.Finish(readings);
        Assert.Equal(4, readings.Loudness.Count);
        Assert.Equal(20, SoundLoudness.Hop * 1000 / SoundReader.SampleRate);
    }
}
