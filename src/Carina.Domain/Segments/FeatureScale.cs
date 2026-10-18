namespace Carina.Domain.Segments;

/// <summary>
/// The mean and standard deviation a feature is standardised with, carried by a version of the model.
/// </summary>
public sealed record FeatureScale
{
    public FeatureScale(double mean, double sd)
    {
        if (!double.IsFinite(mean))
        {
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "A mean is a finite number.");
        }

        if (!double.IsFinite(sd) || sd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sd), sd, "A standard deviation is a finite number above zero.");
        }

        Mean = mean;
        Sd = sd;
    }

    public static FeatureScale Identity { get; } = new(0, 1);

    public double Mean { get; }

    public double Sd { get; }

    public double Standardised(double value) => (value - Mean) / Sd;
}
