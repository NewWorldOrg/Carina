namespace Carina.Domain.Segments;

public readonly record struct ChannelDifference(byte Correlation, byte Difference)
{
    public const int Hop = 800;

    public const int PerChunk = LearningData.ChunkSeconds * SoundReader.SampleRate / Hop;

    public const byte Unmeasured = 255;

    public const int Steps = 100;

    public double? Likeness => Correlation == Unmeasured ? null : (Correlation - Steps) / (double)Steps;

    public static ChannelDifference Of(long leftSquares, long rightSquares, long products, long differenceSquares, int samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);

        byte difference = SoundLoudness.Of(differenceSquares / (4.0 * samples));

        if (leftSquares is 0 || rightSquares is 0)
        {
            return new ChannelDifference(Unmeasured, difference);
        }

        double correlation = Math.Clamp(products / Math.Sqrt((double)leftSquares * rightSquares), -1, 1);

        return new ChannelDifference((byte)Math.Round((correlation + 1) * Steps, MidpointRounding.AwayFromZero), difference);
    }
}
