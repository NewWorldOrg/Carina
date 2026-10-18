using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// Synthetic programmes for matching: a stretch of random tones of its own, with pieces of sound
/// shared with other programmes laid over it at chosen seconds.
/// </summary>
internal static class SoundScenes
{
    public const int SampleRate = SoundReader.SampleRate;

    public static short[] Programme(int seed, int seconds) => SyntheticSound.Tones(seed, seconds * SampleRate);

    public static short[] Piece(int seed, double seconds) => SyntheticSound.Tones(seed, (int)(seconds * SampleRate));

    public static short[] Silence(int seconds) => new short[seconds * SampleRate];

    public static short[] Silence(double seconds) => new short[(int)(seconds * SampleRate)];

    public static short[] Joined(params short[][] pieces) => [.. pieces.SelectMany(piece => piece)];

    public static short[] Tone(double hertz, int seconds) => SyntheticSound.Sine(hertz, 6000, seconds * SampleRate);

    public static short[] Laid(short[] programme, short[] piece, double atSecond, double gain = 1, int noise = 0, int noiseSeed = 0)
    {
        short[] laid = [.. programme];
        Random random = new(noiseSeed);
        int at = (int)(atSecond * SampleRate);

        for (int sample = 0; sample < piece.Length; sample++)
        {
            double value = (piece[sample] * gain) + (noise > 0 ? random.Next(-noise, noise + 1) : 0);
            laid[at + sample] = (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
        }

        return laid;
    }

    public static SoundTrack Track(short[] mono)
    {
        SoundReadings readings = SyntheticSound.Read(SyntheticSound.Mono(mono));

        return SoundTrack.Of([.. readings.Fingerprints], [.. readings.Loudness]);
    }

    public static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
