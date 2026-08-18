namespace Carina.Domain.Channels;

public sealed record RotationBackoff
{
    public static readonly RotationBackoff Default =
        new(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 6);

    public RotationBackoff(TimeSpan firstDelay, int factor, TimeSpan maximumDelay, int failureCeiling)
    {
        if (firstDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstDelay), firstDelay, "The first delay has to be positive.");
        }

        if (factor < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor), factor, "A factor below 2 does not back off.");
        }

        if (maximumDelay < firstDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelay), maximumDelay, "The maximum delay is shorter than the first one.");
        }

        if (failureCeiling < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureCeiling), failureCeiling, "A ceiling below 2 leaves no room to back off.");
        }

        FirstDelay = firstDelay;
        Factor = factor;
        MaximumDelay = maximumDelay;
        FailureCeiling = failureCeiling;
    }

    public TimeSpan FirstDelay { get; }

    public int Factor { get; }

    public TimeSpan MaximumDelay { get; }

    public int FailureCeiling { get; }

    public TimeSpan DelayAfter(int consecutiveFailures)
    {
        if (consecutiveFailures < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveFailures), consecutiveFailures, "There is no delay before the first failure.");
        }

        TimeSpan delay = FirstDelay;

        for (int step = 1; step < consecutiveFailures && delay < MaximumDelay; step++)
        {
            delay *= Factor;
        }

        return delay < MaximumDelay ? delay : MaximumDelay;
    }
}
