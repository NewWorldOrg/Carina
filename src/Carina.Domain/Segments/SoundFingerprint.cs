using System.Numerics;

namespace Carina.Domain.Segments;

public static class SoundFingerprint
{
    public const int Hop = 128;

    public const int Bits = SoundSpectrum.Bands - 1;

    public const int PerChunk = LearningData.ChunkSeconds * SoundReader.SampleRate / Hop;

    public static uint Of(ReadOnlySpan<double> before, ReadOnlySpan<double> now)
    {
        if (before.Length != SoundSpectrum.Bands || now.Length != SoundSpectrum.Bands)
        {
            throw new ArgumentException($"A fingerprint compares two windows of {SoundSpectrum.Bands} bands each.");
        }

        uint fingerprint = 0;

        for (int band = 0; band < Bits; band++)
        {
            double change = now[band] - now[band + 1] - (before[band] - before[band + 1]);

            if (change > 0)
            {
                fingerprint |= 1u << band;
            }
        }

        return fingerprint;
    }

    public static int Disagreeing(uint one, uint other) => BitOperations.PopCount(one ^ other);
}
