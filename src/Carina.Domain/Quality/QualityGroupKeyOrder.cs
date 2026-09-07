using Carina.Contracts;

namespace Carina.Domain.Quality;

internal sealed class QualityGroupKeyOrder : IComparer<QualityGroupKey>
{
    public static readonly QualityGroupKeyOrder Instance = new();

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
