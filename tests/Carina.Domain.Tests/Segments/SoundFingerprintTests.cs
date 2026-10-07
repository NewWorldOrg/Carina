using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SoundFingerprintTests
{
    private const int TenSeconds = 10 * SoundReader.SampleRate;

    [Fact(DisplayName = "the same sound gives the same fingerprints")]
    public void TheSameSoundGivesTheSameFingerprints()
    {
        SoundReadings once = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(11, TenSeconds)));
        SoundReadings again = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(11, TenSeconds)));

        Assert.Equal(once.Fingerprints, again.Fingerprints);
        Assert.Contains(once.Fingerprints, fingerprint => fingerprint is not 0);
    }

    [Theory(DisplayName = "the same sound louder or quieter differs in fewer than a tenth of its fingerprint bits")]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.5)]
    public void TheSameSoundAtAnotherVolumeDiffersInFewBits(double gain)
    {
        SoundReadings original = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(11, TenSeconds)));
        SoundReadings scaled = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(11, TenSeconds, gain)));

        Assert.True(DisagreeingShare(original.Fingerprints, scaled.Fingerprints) < 0.1);
    }

    [Fact(DisplayName = "a different sound differs in at least four tenths of its fingerprint bits")]
    public void ADifferentSoundDiffersInManyBits()
    {
        SoundReadings one = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(11, TenSeconds)));
        SoundReadings other = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(12, TenSeconds)));

        Assert.True(DisagreeingShare(one.Fingerprints, other.Fingerprints) >= 0.4);
    }

    [Fact(DisplayName = "a fingerprint is written once the whole window from its step has arrived, one every 16 ms")]
    public void AFingerprintIsWrittenOnceItsWindowHasArrived()
    {
        short[] sound = SyntheticSound.Mono(SyntheticSound.Tones(3, SoundSpectrum.Window + (2 * SoundFingerprint.Hop)));
        SoundReader reader = new();
        SoundReadings readings = new();

        reader.Hear(sound.AsSpan(0, (SoundSpectrum.Window - 1) * 2), readings);
        Assert.Empty(readings.Fingerprints);

        reader.Hear(sound.AsSpan((SoundSpectrum.Window - 1) * 2, 2), readings);
        Assert.Single(readings.Fingerprints);

        reader.Hear(sound.AsSpan(SoundSpectrum.Window * 2), readings);
        Assert.Equal(3, readings.Fingerprints.Count);
        Assert.Equal(128, SoundFingerprint.Hop);
        Assert.Equal(16, SoundFingerprint.Hop * 1000 / SoundReader.SampleRate);
    }

    [Theory(DisplayName = "once the sound is finished there is a fingerprint for every step it began, the rest of the window heard as silence")]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(128, 1)]
    [InlineData(129, 2)]
    [InlineData(5000, 40)]
    public void FinishingWritesAFingerprintForEveryStepBegun(int samples, int fingerprints)
    {
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Mono(SyntheticSound.Tones(5, samples)));

        Assert.Equal(fingerprints, readings.Fingerprints.Count);
    }

    [Fact(DisplayName = "silence has fingerprints with no bit set")]
    public void SilenceHasEmptyFingerprints()
    {
        SoundReadings readings = SyntheticSound.Read(new short[2 * TenSeconds]);

        Assert.All(readings.Fingerprints, fingerprint => Assert.Equal(0u, fingerprint));
    }

    [Fact(DisplayName = "the fingerprint compares each pair of neighbouring bands with the same pair in the window before")]
    public void TheFingerprintComparesNeighbouringBands()
    {
        double[] before = new double[SoundSpectrum.Bands];
        double[] now = new double[SoundSpectrum.Bands];
        now[0] = 1;
        now[5] = 1;

        uint fingerprint = SoundFingerprint.Of(before, now);

        Assert.Equal((1u << 0) | (1u << 5), fingerprint);
        Assert.Equal(1u << 4, SoundFingerprint.Of(now, before) & (1u << 4));
        Assert.Equal(32, SoundFingerprint.Bits);
        Assert.Equal(2, SoundFingerprint.Disagreeing(0b1010, 0b0110));
    }

    private static double DisagreeingShare(List<uint> one, List<uint> other)
    {
        Assert.Equal(one.Count, other.Count);

        long disagreeing = one.Zip(other, SoundFingerprint.Disagreeing).Sum();

        return (double)disagreeing / (one.Count * SoundFingerprint.Bits);
    }
}
