namespace Carina.Domain.Segments;

/// <summary>
/// The intercept and the weights of the standardised features one kind of segment is scored with
/// against the programme. A feature without a weight weighs nothing.
/// </summary>
public sealed class SegmentClassWeights : IEquatable<SegmentClassWeights>
{
    public SegmentClassWeights(double bias, IReadOnlyDictionary<string, double> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (!double.IsFinite(bias))
        {
            throw new ArgumentOutOfRangeException(nameof(bias), bias, "An intercept is a finite number.");
        }

        foreach ((string name, double weight) in features)
        {
            if (!SegmentFeature.IsKnown(name))
            {
                throw new ArgumentException($"No feature is called {name}.", nameof(features));
            }

            if (!double.IsFinite(weight))
            {
                throw new ArgumentOutOfRangeException(nameof(features), weight, "A weight is a finite number.");
            }
        }

        Bias = bias;
        Features = SortedMaps.Copied(features, StringComparer.Ordinal);
    }

    public double Bias { get; }

    public IReadOnlyDictionary<string, double> Features { get; }

    public bool Equals(SegmentClassWeights? other)
        => other is not null && Bias.Equals(other.Bias) && SortedMaps.Same(Features, other.Features);

    public override bool Equals(object? obj) => Equals(obj as SegmentClassWeights);

    public override int GetHashCode() => HashCode.Combine(Bias, SortedMaps.Hash(Features));
}
