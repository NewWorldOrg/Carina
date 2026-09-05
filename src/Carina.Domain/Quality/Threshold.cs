using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed record Threshold
{
    private Threshold(double @default, double current, bool provisional, long observations, DateTime updatedAt)
    {
        Default = @default;
        Current = current;
        Provisional = provisional;
        Observations = observations;
        UpdatedAt = updatedAt;
    }

    public double Default { get; }

    public double Current { get; }

    public bool Provisional { get; }

    public long Observations { get; }

    public DateTime UpdatedAt { get; }

    public bool IsAsShipped => Current.Equals(Default);

    public static Threshold Provisionally(double value, long observations, DateTime updatedAt)
        => Of(value, value, provisional: true, observations, updatedAt);

    public static Threshold Of(double @default, double current, bool provisional, long observations, DateTime updatedAt)
    {
        Measured(@default, nameof(@default));
        Measured(current, nameof(current));
        ArgumentOutOfRangeException.ThrowIfNegative(observations);

        if (!provisional && observations is 0)
        {
            throw new ArgumentException(
                "A threshold that is not provisional stands on measurement, so it cannot stand on none.",
                nameof(observations));
        }

        return new Threshold(@default, current, provisional, observations, UtcTimes.Required(updatedAt, nameof(updatedAt)));
    }

    private static void Measured(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A threshold is a number a reading can be compared against.");
        }
    }
}
