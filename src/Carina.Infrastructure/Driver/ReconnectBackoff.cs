namespace Carina.Infrastructure.Driver;

public sealed class ReconnectBackoff
{
    private const int ExponentCap = 20;

    private readonly TimeSpan firstDelay;
    private readonly TimeSpan delayCap;
    private readonly Func<double> chance;

    private int failures;

    public ReconnectBackoff(TimeSpan firstDelay, TimeSpan delayCap, Func<double>? chance = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(firstDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(delayCap, firstDelay);

        this.firstDelay = firstDelay;
        this.delayCap = delayCap;
        this.chance = chance ?? Random.Shared.NextDouble;
    }

    public TimeSpan Next()
    {
        int exponent = Math.Min(failures, ExponentCap);
        failures = exponent + 1;

        double raw = Math.Min(
            firstDelay.TotalMilliseconds * Math.Pow(2, exponent),
            delayCap.TotalMilliseconds);
        double factor = 0.5 + (0.5 * Math.Clamp(chance(), 0, 1));

        return TimeSpan.FromMilliseconds(raw * factor);
    }

    public void Reset() => failures = 0;
}
