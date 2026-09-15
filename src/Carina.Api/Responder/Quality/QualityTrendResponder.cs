using Carina.Api.Services;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityTrendLayerResponder(int Layer, double Highest)
{
    public static QualityTrendLayerResponder Of(LayerErrorPeak peak)
    {
        ArgumentNullException.ThrowIfNull(peak);

        return new QualityTrendLayerResponder(peak.Layer, peak.Highest);
    }
}

public sealed record QualityTrendPointResponder(
    DateTime From,
    DateTime Until,
    QualityReadingResponder Reading,
    double? Worst,
    double Level,
    IReadOnlyList<QualityTrendLayerResponder> Layers)
{
    public static QualityTrendPointResponder Of(QualityTrendPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        return new QualityTrendPointResponder(
            point.From,
            point.Until,
            QualityReadingResponder.Of(point.Reading),
            point.Worst,
            point.Level,
            [.. point.Layers.Select(QualityTrendLayerResponder.Of)]);
    }
}

public sealed record QualityTrendChannelResponder(
    int NetworkId,
    int ServiceId,
    int? TransportStreamId,
    TuneSystem? Kind,
    IReadOnlyList<int> ServiceIds)
{
    public static QualityTrendChannelResponder Of(QualityTrendChannel channel, BroadcastStream? carrier)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return new QualityTrendChannelResponder(
            channel.Network.Value,
            channel.Service.Value,
            carrier?.TransportStreamId.Value,
            carrier?.Tuning.System,
            carrier is null ? [channel.Service.Value] : [.. carrier.Services.Select(service => service.Value)]);
    }
}

public sealed record QualityTrendSeriesResponder(
    QualityTrendChannelResponder? Channel,
    IReadOnlyList<QualityTrendPointResponder> Points)
{
    public static QualityTrendSeriesResponder Of(QualityTrendRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new QualityTrendSeriesResponder(
            row.Series.Channel is { } channel ? QualityTrendChannelResponder.Of(channel, row.Carrier) : null,
            [.. row.Series.Points.Select(QualityTrendPointResponder.Of)]);
    }
}

public sealed record QualityTrendResponder(
    QualityPeriodResponder Period,
    QualityTrendSubject Subject,
    QualityTrendStep Step,
    int MostPoints,
    IReadOnlyList<QualityTrendSeriesResponder> Series,
    bool Provisional)
{
    public static QualityTrendResponder Of(QualityTrendView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new QualityTrendResponder(
            QualityPeriodResponder.Of(view.Frame.Period),
            view.Subject,
            view.Frame.Step,
            QualityTrendFrame.MostPoints,
            [.. view.Rows.Select(QualityTrendSeriesResponder.Of)],
            view.Provisional);
    }
}
