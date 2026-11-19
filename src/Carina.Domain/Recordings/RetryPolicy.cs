namespace Carina.Domain.Recordings;

public sealed record RetryPolicy
{
    public const int MostAttemptsItAllows = 20;

    public static readonly TimeSpan LongestBetween = TimeSpan.FromDays(1);

    public static readonly RetryPolicy Default = new(10, TimeSpan.FromMinutes(1));

    public RetryPolicy(int mostAttempts, TimeSpan betweenAttempts)
    {
        if (mostAttempts is < 0 or > MostAttemptsItAllows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostAttempts),
                mostAttempts,
                $"A start is tried again no fewer than 0 and no more than {MostAttemptsItAllows} times.");
        }

        if (betweenAttempts <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenAttempts),
                betweenAttempts,
                "Trying a start again with no pause spends every attempt on the same failure.");
        }

        if (betweenAttempts > LongestBetween)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenAttempts),
                betweenAttempts,
                $"A pause longer than {LongestBetween} outlasts any programme the attempt could still reach.");
        }

        MostAttempts = mostAttempts;
        BetweenAttempts = betweenAttempts;
    }

    public int MostAttempts { get; }

    public TimeSpan BetweenAttempts { get; }
}
