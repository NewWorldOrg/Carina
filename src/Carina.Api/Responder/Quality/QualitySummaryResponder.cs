using Carina.Api.Services;

using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityPeriodResponder(DateTime From, DateTime Until)
{
    public static QualityPeriodResponder Of(QualityPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return new QualityPeriodResponder(period.From, period.Until);
    }
}

public sealed record QualitySummaryResponder(
    QualityPeriodResponder Period,
    int Recordings,
    IReadOnlyList<QualityMeasureResponder> Measures,
    IReadOnlyList<QualitySignalResponder> Signal,
    bool Provisional)
{
    public static QualitySummaryResponder Of(QualitySummaryView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new QualitySummaryResponder(
            QualityPeriodResponder.Of(view.Period),
            view.Recordings,
            QualityMeasureResponder.Over(view.Measures),
            QualitySignalResponder.Over(view.Signal),
            view.Provisional);
    }
}
