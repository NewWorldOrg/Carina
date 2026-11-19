namespace Carina.Domain.Segments;

public sealed class SegmentFeatureValues
{
    private readonly Dictionary<string, double?> values;

    private SegmentFeatureValues(Dictionary<string, double?> values)
    {
        this.values = values;
    }

    public double? this[string name] => values.GetValueOrDefault(name);

    public static SegmentFeatureValues Of(IReadOnlyDictionary<string, double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Dictionary<string, double?> kept = new(StringComparer.Ordinal);

        foreach ((string name, double? value) in values)
        {
            if (!SegmentFeature.IsKnown(name))
            {
                throw new ArgumentException($"No feature is called {name}.", nameof(values));
            }

            if (value is double given && !double.IsFinite(given))
            {
                throw new ArgumentOutOfRangeException(nameof(values), value, "A feature is a finite number or not known.");
            }

            kept[name] = value;
        }

        return new SegmentFeatureValues(kept);
    }
}
