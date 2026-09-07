using Carina.Api.Services;

using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityChannelResponder(
    int NetworkId,
    int ServiceId,
    TuneSystem? Kind,
    IReadOnlyList<QualityMeasureResponder> Measures)
{
    public static QualityChannelResponder Of(QualityGroupReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new QualityChannelResponder(
            reading.Key.Network!.Value,
            reading.Key.Service!.Value,
            reading.Key.Kind,
            QualityMeasureResponder.Over(reading.Measures));
    }
}

public sealed record QualityChannelListResponder(
    QualityPeriodResponder Period,
    IReadOnlyList<QualityMetric> Metrics,
    IReadOnlyList<QualityChannelResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage,
    bool Provisional)
{
    public static QualityChannelListResponder Of(QualityGroupPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new QualityChannelListResponder(
            QualityPeriodResponder.Of(page.Period),
            page.Metrics,
            [.. page.Found.Items.Select(QualityChannelResponder.Of)],
            page.Found.Total,
            page.Found.CurrentPage,
            page.Found.LastPage,
            page.Found.PerPage,
            page.Provisional);
    }
}
