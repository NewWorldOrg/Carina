using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Domain.Segments;

/// <summary>
/// The parameters of one version of the model segments are classified by: the weights of the logistic
/// regression, an offset per station (keyed <c>network id:service id</c>) and per series name, the
/// thresholds, and the mean and standard deviation each feature is standardised with. Every feature the
/// weights name has a scale. A kind weighed against the programme is scored by its own weights, and a break
/// by both offsets besides, which tell how much more often a station or a series breaks for adverts.
/// <see cref="First"/> is the version made by hand from the rule-based detection before any learning: a piece
/// of 15-second multiples between two silent cuts, out of the logo, is a break. <see cref="FirstOfFourKinds"/>
/// weighs the break the same way and adds the opening and the ending made by hand, which earlier episodes of the
/// series having one at the same place make likelier but never enough on their own. What the catalog hears is
/// fixed when the sequence is decided (<see cref="SegmentSequence"/>), not weighed.
/// </summary>
public sealed class SegmentModel : IEquatable<SegmentModel>
{
    public SegmentModel(
        SegmentWeights weights,
        IReadOnlyDictionary<string, double> stationOffsets,
        IReadOnlyDictionary<string, double> seriesOffsets,
        SegmentThresholds thresholds,
        IReadOnlyDictionary<string, FeatureScale> standardisation)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(stationOffsets);
        ArgumentNullException.ThrowIfNull(seriesOffsets);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(standardisation);

        Offsets(stationOffsets, IsAStation, "A station is keyed by its network id and service id joined by a colon.");
        Offsets(seriesOffsets, IsASeries, $"A series is keyed by its name, in 1 to {Reservation.NameMaxLength} characters.");
        Scaled(weights, standardisation);

        Weights = weights;
        StationOffsets = SortedMaps.Copied(stationOffsets, StringComparer.Ordinal);
        SeriesOffsets = SortedMaps.Copied(seriesOffsets, StringComparer.Ordinal);
        Thresholds = thresholds;
        Standardisation = SortedMaps.Copied(standardisation, StringComparer.Ordinal);
    }

    public static SegmentModel First { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = new(-2.5, new Dictionary<string, double>
            {
                [SegmentFeature.IsMultipleOf15s] = 2.0,
                [SegmentFeature.EdgeSilence] = 1.5,
                [SegmentFeature.LogoShare] = -3.0,
            }),
        }),
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        SegmentThresholds.Initial,
        new Dictionary<string, FeatureScale>
        {
            [SegmentFeature.IsMultipleOf15s] = FeatureScale.Identity,
            [SegmentFeature.EdgeSilence] = FeatureScale.Identity,
            [SegmentFeature.LogoShare] = FeatureScale.Identity,
        });

    public static SegmentModel FirstOfFourKinds { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = First.Weights.Classes[SegmentKind.Break],
            [SegmentKind.Opening] = new(-6.0, new Dictionary<string, double>
            {
                [SegmentFeature.PastOpeningShare] = 4.0,
            }),
            [SegmentKind.Ending] = new(-6.0, new Dictionary<string, double>
            {
                [SegmentFeature.PastEndingShare] = 4.0,
            }),
        }),
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        SegmentThresholds.Initial,
        new Dictionary<string, FeatureScale>
        {
            [SegmentFeature.IsMultipleOf15s] = FeatureScale.Identity,
            [SegmentFeature.EdgeSilence] = FeatureScale.Identity,
            [SegmentFeature.LogoShare] = FeatureScale.Identity,
            [SegmentFeature.PastOpeningShare] = FeatureScale.Identity,
            [SegmentFeature.PastEndingShare] = FeatureScale.Identity,
        });

    public SegmentWeights Weights { get; }

    public IReadOnlyDictionary<string, double> StationOffsets { get; }

    public IReadOnlyDictionary<string, double> SeriesOffsets { get; }

    public SegmentThresholds Thresholds { get; }

    public IReadOnlyDictionary<string, FeatureScale> Standardisation { get; }

    public int Kinds => Weights.Classes.Count + 1;

    public static string StationKey(NetworkId networkId, ServiceId serviceId)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return string.Create(CultureInfo.InvariantCulture, $"{networkId.Value}:{serviceId.Value}");
    }

    /// <summary>
    /// The chance a score stands for, by the logistic function.
    /// </summary>
    public static double Chance(double score) => score >= 0 ? 1 / (1 + Math.Exp(-score)) : Math.Exp(score) / (1 + Math.Exp(score));

    /// <summary>
    /// The score of a segment being a break rather than programme.
    /// </summary>
    public double BreakScore(SegmentFeatureValues features, string station, string? series) => Score(SegmentKind.Break, features, station, series);

    /// <summary>
    /// How likely a segment is to be each kind the version tells apart.
    /// </summary>
    public SegmentChances Chances(SegmentFeatureValues features, string station, string? series)
        => SegmentChances.Of(Weights.Classes.Keys.ToDictionary(kind => kind, kind => Score(kind, features, station, series)));

    /// <summary>
    /// The score of a segment being <paramref name="kind"/> rather than programme: the intercept, each weighted
    /// feature standardised (one not known counts as its mean), and for a break the offsets of the station and
    /// the series; nothing for the programme itself.
    /// </summary>
    public double Score(SegmentKind kind, SegmentFeatureValues features, string station, string? series)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(station);

        if (kind is SegmentKind.Programme)
        {
            return 0;
        }

        if (!Weights.Classes.TryGetValue(kind, out SegmentClassWeights? weights))
        {
            throw new ArgumentException("A kind is scored by a version that weighs it.", nameof(kind));
        }

        double score = weights.Bias;

        if (kind is SegmentKind.Break)
        {
            score += StationOffsets.GetValueOrDefault(station);

            if (series is not null)
            {
                score += SeriesOffsets.GetValueOrDefault(series);
            }
        }

        foreach ((string name, double weight) in weights.Features)
        {
            if (features[name] is double value)
            {
                score += weight * Standardisation[name].Standardised(value);
            }
        }

        return score;
    }

    public bool Equals(SegmentModel? other)
        => other is not null
           && Weights.Equals(other.Weights)
           && SortedMaps.Same(StationOffsets, other.StationOffsets)
           && SortedMaps.Same(SeriesOffsets, other.SeriesOffsets)
           && Thresholds.Equals(other.Thresholds)
           && SortedMaps.Same(Standardisation, other.Standardisation);

    public override bool Equals(object? obj) => Equals(obj as SegmentModel);

    public override int GetHashCode()
        => HashCode.Combine(
            Weights,
            SortedMaps.Hash(StationOffsets),
            SortedMaps.Hash(SeriesOffsets),
            Thresholds,
            SortedMaps.Hash(Standardisation));

    private static void Offsets(IReadOnlyDictionary<string, double> offsets, Func<string, bool> keyed, string meaning)
    {
        foreach ((string key, double offset) in offsets)
        {
            if (!keyed(key))
            {
                throw new ArgumentException(meaning, nameof(offsets));
            }

            if (!double.IsFinite(offset))
            {
                throw new ArgumentOutOfRangeException(nameof(offsets), offset, "An offset is a finite number.");
            }
        }
    }

    private static void Scaled(SegmentWeights weights, IReadOnlyDictionary<string, FeatureScale> standardisation)
    {
        if (standardisation.Any(pair => !SegmentFeature.IsKnown(pair.Key) || pair.Value is null))
        {
            throw new ArgumentException("A scale is kept for a feature the detection computes.", nameof(standardisation));
        }

        if (weights.Classes.Values.SelectMany(weighed => weighed.Features.Keys).Any(name => !standardisation.ContainsKey(name)))
        {
            throw new ArgumentException("Every feature the weights name has a scale to standardise it with.", nameof(standardisation));
        }
    }

    internal static bool IsAStation(string key)
    {
        string[] parts = key.Split(':');

        return parts.Length is 2 && parts.All(IsAnId);
    }

    private static bool IsAnId(string part)
        => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id is >= NetworkId.MinValue and <= NetworkId.MaxValue;

    internal static bool IsASeries(string key) => key.Length is > 0 and <= Reservation.NameMaxLength;
}
