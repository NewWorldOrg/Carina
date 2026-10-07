namespace Carina.Domain.Segments;

/// <summary>
/// How much learning data is kept: the recordings it was taken from, how long of them was read, the room the
/// segment tables take in the database, and the ended recordings still waiting to be read.
/// </summary>
public sealed record LearningDataAmount(int Recordings, TimeSpan Duration, long Bytes, int Waiting)
{
    public static LearningDataAmount None { get; } = new(0, TimeSpan.Zero, 0, 0);
}
