namespace Carina.Domain.Segments;

/// <summary>
/// What every piece of learning data is measured against: the version of the calculations that
/// made it, and the length of the stretch of a recording one chunk of it covers.
/// </summary>
public static class LearningData
{
    public const int ExtractionVersion = 1;

    public const int ChunkSeconds = 600;

    public static TimeSpan ChunkStarts(int chunk) => TimeSpan.FromSeconds((long)ChunkSeconds * chunk);
}
