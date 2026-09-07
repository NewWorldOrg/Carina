using Carina.Api.Services;

using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityTunerResponder(
    string? DeviceId,
    IReadOnlyList<QualityMeasureResponder> Measures,
    IReadOnlyList<QualitySignalResponder> Signal)
{
    public static QualityTunerResponder Of(QualityTunerReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new QualityTunerResponder(
            reading.Group.Key.Tuner?.Value,
            QualityMeasureResponder.Over(reading.Group.Measures),
            QualitySignalResponder.Over(reading.Signal));
    }
}

public sealed record QualityTunerListResponder(
    QualityPeriodResponder Period,
    IReadOnlyList<QualityMetric> Metrics,
    IReadOnlyList<QualityTunerResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage,
    bool Provisional)
{
    public static QualityTunerListResponder Of(QualityTunerPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new QualityTunerListResponder(
            QualityPeriodResponder.Of(page.Period),
            page.Metrics,
            [.. page.Found.Items.Select(QualityTunerResponder.Of)],
            page.Found.Total,
            page.Found.CurrentPage,
            page.Found.LastPage,
            page.Found.PerPage,
            page.Provisional);
    }
}
