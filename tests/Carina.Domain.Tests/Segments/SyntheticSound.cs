using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

internal static class SyntheticSound
{
    public static short[] Tones(int seed, int samples, double gain = 1)
    {
        Random random = new(seed);
        short[] sound = new short[samples];
        int at = 0;

        while (at < samples)
        {
            int length = random.Next(400, 1200);
            int[] hertz = [random.Next(250, 2100), random.Next(250, 2100), random.Next(250, 2100)];
            int[] amplitudes = [random.Next(500, 6000), random.Next(500, 6000), random.Next(500, 6000)];

            for (int sample = at; sample < Math.Min(samples, at + length); sample++)
            {
                double value = random.Next(-300, 300) + Played(hertz, amplitudes, sample);
                sound[sample] = Clamped(value * gain);
            }

            at += length;
        }

        return sound;
    }

    public static short[] Sine(double hertz, double amplitude, int samples)
        => [.. Enumerable.Range(0, samples).Select(sample => Clamped(amplitude * Math.Sin(2 * Math.PI * hertz * sample / SoundReader.SampleRate)))];

    public static short[] Interleaved(short[] left, short[] right)
    {
        short[] both = new short[left.Length * 2];

        for (int sample = 0; sample < left.Length; sample++)
        {
            both[2 * sample] = left[sample];
            both[(2 * sample) + 1] = right[sample];
        }

        return both;
    }

    public static short[] Mono(short[] sound) => Interleaved(sound, sound);

    public static short[] Mixed(short[] one, double oneShare, short[] other)
        => [.. one.Zip(other, (first, second) => Clamped((first * oneShare) + (second * (1 - oneShare))))];

    public static SoundReadings Read(short[] interleaved)
    {
        SoundReader reader = new();
        SoundReadings readings = new();

        reader.Hear(interleaved, readings);
        reader.Finish(readings);

        return readings;
    }

    private static double Played(int[] hertz, int[] amplitudes, int sample)
    {
        double value = 0;

        for (int tone = 0; tone < hertz.Length; tone++)
        {
            value += amplitudes[tone] * Math.Sin(2 * Math.PI * hertz[tone] * sample / SoundReader.SampleRate);
        }

        return value;
    }

    private static short Clamped(double value) => (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
}
