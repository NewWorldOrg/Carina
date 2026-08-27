namespace Carina.Infrastructure.Recordings;

public sealed record RecordingWatchSettings
{
    public static readonly RecordingWatchSettings Default = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        5,
        TimeSpan.FromSeconds(2),
        3);

    public RecordingWatchSettings(
        TimeSpan beforeFirstWatch,
        TimeSpan betweenWatches,
        int attemptsAtReopening,
        TimeSpan betweenReopenings,
        int attemptsAtACollision)
    {
        if (beforeFirstWatch <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beforeFirstWatch),
                beforeFirstWatch,
                "The watch waits for the driver to say who it is before its first pass, so it waits for some time.");
        }

        if (betweenWatches <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenWatches),
                betweenWatches,
                "A pass that follows the one before it with no gap is a loop with nothing between its turns.");
        }

        if (attemptsAtReopening < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptsAtReopening),
                attemptsAtReopening,
                "A recording that lost its stream is opened again at least once, or it is never opened at all.");
        }

        if (betweenReopenings <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenReopenings),
                betweenReopenings,
                "Opening the stream again with no pause spends every attempt on the same instant.");
        }

        if (attemptsAtACollision < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptsAtACollision),
                attemptsAtACollision,
                "A count that collides with the row it is written to is written at least once.");
        }

        BeforeFirstWatch = beforeFirstWatch;
        BetweenWatches = betweenWatches;
        AttemptsAtReopening = attemptsAtReopening;
        BetweenReopenings = betweenReopenings;
        AttemptsAtACollision = attemptsAtACollision;
    }

    public TimeSpan BeforeFirstWatch { get; }

    public TimeSpan BetweenWatches { get; }

    public int AttemptsAtReopening { get; }

    public TimeSpan BetweenReopenings { get; }

    public int AttemptsAtACollision { get; }
}
