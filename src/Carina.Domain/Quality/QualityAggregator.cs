using Carina.Contracts;

namespace Carina.Domain.Quality;

public static class QualityAggregator
{
    private const QualityAxis EveryAxis = QualityAxis.Channel | QualityAxis.Tuner | QualityAxis.TimeOfDay | QualityAxis.Kind;

    public static QualityTally Tally(IReadOnlyList<QualityObservation> observations)
        => QualityTally.Over(Checked(observations));

    public static IReadOnlyList<QualityGrouping> GroupBy(IReadOnlyList<QualityObservation> observations, QualityAxis axis)
    {
        Checked(observations);
        Named(axis);

        return
        [
            .. observations
                .GroupBy(observation => QualityGroupKey.Reduced(observation.Facet, axis))
                .Select(group => new QualityGrouping(group.Key, QualityTally.Over([.. group])))
                .OrderBy(grouping => grouping.Key, InTheOrderTheyAreNamed.Instance),
        ];
    }

    public static IReadOnlyList<QualityGrouping> Rank(
        IReadOnlyList<QualityObservation> observations,
        QualityAxis axis,
        ThresholdSense sense,
        int take)
    {
        ThresholdBand.Named(sense);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        int worstFirst = sense is ThresholdSense.Ceiling ? -1 : 1;

        return
        [
            .. GroupBy(observations, axis)
                .Where(grouping => grouping.Tally.Measured > 0)
                .OrderBy(grouping => grouping.Tally.Worst(sense)!.Value * worstFirst)
                .ThenByDescending(grouping => grouping.Tally.BeyondThreshold)
                .ThenByDescending(grouping => grouping.Tally.Measured)
                .ThenBy(grouping => grouping.Key, InTheOrderTheyAreNamed.Instance)
                .Take(take),
        ];
    }

    private static IReadOnlyList<QualityObservation> Checked(IReadOnlyList<QualityObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        foreach (QualityObservation observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation, nameof(observations));
        }

        return observations;
    }

    private static void Named(QualityAxis axis)
    {
        if ((axis & ~EveryAxis) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Observations are gathered along the axes this domain names.");
        }
    }

    private sealed class InTheOrderTheyAreNamed : IComparer<QualityGroupKey>
    {
        public static readonly InTheOrderTheyAreNamed Instance = new();

        public int Compare(QualityGroupKey? x, QualityGroupKey? y)
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            int settled = Nullable.Compare((TuneSystem?)x.Kind, y.Kind);

            if (settled is not 0)
            {
                return settled;
            }

            settled = Nullable.Compare(x.Network?.Value, y.Network?.Value);

            if (settled is not 0)
            {
                return settled;
            }

            settled = Nullable.Compare(x.Service?.Value, y.Service?.Value);

            if (settled is not 0)
            {
                return settled;
            }

            settled = string.CompareOrdinal(x.Tuner?.Value, y.Tuner?.Value);

            return settled is not 0 ? settled : Nullable.Compare(x.HourOfDay, y.HourOfDay);
        }
    }
}
