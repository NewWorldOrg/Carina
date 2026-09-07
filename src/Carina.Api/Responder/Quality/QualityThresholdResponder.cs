using Carina.Api.Services;

using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityThresholdChangeResponder(
    double PreviousValue,
    double NextValue,
    DateTime ChangedAt,
    string? ChangedBy)
{
    public static QualityThresholdChangeResponder Of(QualityThresholdChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new QualityThresholdChangeResponder(
            change.PreviousValue,
            change.NextValue,
            change.ChangedAt,
            change.ChangedBy);
    }
}

public sealed record QualityThresholdResponder(
    QualityThresholdKey Key,
    QualityMetric? Metric,
    ThresholdSense Sense,
    double DefaultValue,
    double CurrentValue,
    double Lowest,
    double Highest,
    bool Provisional,
    long Observations,
    bool Stored,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    QualityThresholdChangeResponder? LastChange)
{
    public static QualityThresholdResponder Of(QualityThresholdBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        QualityThresholdStanding standing = book.Standing;

        return new QualityThresholdResponder(
            standing.Key,
            standing.Shape.Metric,
            standing.Shape.Sense,
            standing.Setting.Default,
            standing.Setting.Current,
            standing.Shape.Lowest,
            standing.Shape.Highest,
            standing.Setting.Provisional,
            standing.Setting.Observations,
            standing.Stored,
            standing.Stored ? standing.Setting.UpdatedAt : null,
            standing.UpdatedBy,
            book.LastChange is { } change ? QualityThresholdChangeResponder.Of(change) : null);
    }
}

public sealed record QualityThresholdListResponder(IReadOnlyList<QualityThresholdResponder> Items)
{
    public static QualityThresholdListResponder Of(IReadOnlyList<QualityThresholdBook> books)
    {
        ArgumentNullException.ThrowIfNull(books);

        return new QualityThresholdListResponder([.. books.Select(QualityThresholdResponder.Of)]);
    }
}
