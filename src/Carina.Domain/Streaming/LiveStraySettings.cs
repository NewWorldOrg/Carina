namespace Carina.Domain.Streaming;

public sealed record LiveStraySettings
{
    private readonly TimeSpan beforeFirstSweep = TimeSpan.FromSeconds(5);

    private readonly TimeSpan betweenSweeps = TimeSpan.FromSeconds(60);

    public TimeSpan BeforeFirstSweep
    {
        get => beforeFirstSweep;

        init => beforeFirstSweep = value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The first sweep is waited for some time or none, and never for less than none.");
    }

    public TimeSpan BetweenSweeps
    {
        get => betweenSweeps;

        init => betweenSweeps = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A sweep follows the one before it after some time, not none, or it is a spin rather than a sweep.");
    }
}
