namespace Carina.Domain.Segments;

/// <summary>
/// One labelled segment to learn from: its features, its station and series, whether it is a break, and
/// the weight its label carries.
/// </summary>
public sealed record SegmentSample
{
    public SegmentSample(SegmentFeatureValues features, string station, string? series, bool isBreak, double weight)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(station);

        if (!SegmentModel.IsAStation(station))
        {
            throw new ArgumentException("A station is keyed by its network id and service id joined by a colon.", nameof(station));
        }

        if (series is not null && !SegmentModel.IsASeries(series))
        {
            throw new ArgumentException("A series is named in some characters, or not at all.", nameof(series));
        }

        if (!double.IsFinite(weight) || weight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "A label weighs a finite amount above nothing.");
        }

        Features = features;
        Station = station;
        Series = series;
        IsBreak = isBreak;
        Weight = weight;
    }

    public SegmentFeatureValues Features { get; }

    public string Station { get; }

    public string? Series { get; }

    public bool IsBreak { get; }

    public double Weight { get; }
}
