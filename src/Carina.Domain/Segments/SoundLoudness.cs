namespace Carina.Domain.Segments;

public static class SoundLoudness
{
    public const int Hop = 160;

    public const int PerChunk = LearningData.ChunkSeconds * SoundReader.SampleRate / Hop;

    public const byte Silent = 255;

    public const double StepDecibels = 0.5;

    private const double FullScalePower = 32768.0 * 32768.0;

    public static byte Of(double meanSquare)
    {
        if (meanSquare <= 0)
        {
            return Silent;
        }

        double below = -10 * Math.Log10(meanSquare / FullScalePower);

        return (byte)Math.Clamp(Math.Round(below / StepDecibels, MidpointRounding.AwayFromZero), 0, Silent);
    }

    public static double DecibelsBelowFullScale(byte loudness) => loudness * StepDecibels;
}
