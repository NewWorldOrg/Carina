using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityThresholdHistory
{
    private readonly IReadOnlyList<QualityThresholdStanding> standing;

    private readonly IReadOnlyList<QualityThresholdChange> changes;

    private QualityThresholdHistory(
        IReadOnlyList<QualityThresholdStanding> standing,
        IReadOnlyList<QualityThresholdChange> changes)
    {
        this.standing = standing;
        this.changes = changes;
    }

    public static QualityThresholdHistory Of(
        IReadOnlyList<QualityThresholdStanding> standing,
        IReadOnlyList<QualityThresholdChange> changes)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(changes);

        return new QualityThresholdHistory(standing, [.. changes.OrderBy(change => change.ChangedAt)]);
    }

    public IReadOnlyList<QualityThresholdStanding> At(DateTime at)
    {
        UtcTimes.Required(at, nameof(at));

        return [.. standing.Select(held => AsItStood(held, at))];
    }

    private QualityThresholdStanding AsItStood(QualityThresholdStanding held, DateTime at)
    {
        if (changes.FirstOrDefault(change => change.Key == held.Key && change.ChangedAt >= at) is not { } later)
        {
            return held;
        }

        return held with
        {
            Setting = Threshold.Of(
                held.Setting.Default,
                later.PreviousValue,
                held.Setting.Provisional,
                held.Setting.Observations,
                held.Setting.UpdatedAt),
        };
    }
}
