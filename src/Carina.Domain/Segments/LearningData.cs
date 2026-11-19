namespace Carina.Domain.Segments;

public static class LearningData
{
    public const int ExtractionVersion = 2;

    public const int ChunkSeconds = 600;

    public const int LastChunk = int.MaxValue / ChunkSeconds;

    public static TimeSpan ChunkStarts(int chunk) => TimeSpan.FromSeconds((long)ChunkSeconds * chunk);
}
