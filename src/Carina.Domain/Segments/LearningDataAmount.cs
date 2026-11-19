namespace Carina.Domain.Segments;

public sealed record LearningDataAmount(int Recordings, TimeSpan Duration, long Bytes, int Waiting)
{
    public static LearningDataAmount None { get; } = new(0, TimeSpan.Zero, 0, 0);
}
