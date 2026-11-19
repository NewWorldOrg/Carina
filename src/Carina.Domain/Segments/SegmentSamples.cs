namespace Carina.Domain.Segments;

public static class SegmentSamples
{
    public static IReadOnlyList<SegmentSample> Of(
        RecordingPieces read,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<CatalogHeard>? touching = null,
        ViewerSeeks? seeks = null,
        SegmentDetection? inUse = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        string? series = read.Series is string named && SegmentModel.IsASeries(named) ? named : null;
        IReadOnlyList<SegmentLabel?> pieces = SegmentLabels.Of(read.Pieces, context, actions, touching, seeks, inUse);
        IReadOnlyList<SegmentLabel?> joined = SegmentLabels.OfJoined(read.Joined, context, actions, seeks);

        return
        [
            .. read.Pieces.Zip(pieces).Select(pair => Sample(pair.First, pair.Second, read.Station, series)).OfType<SegmentSample>(),
            .. read.Joined.Zip(joined).Select(pair => Sample(pair.First.Piece, pair.Second, read.Station, series)).OfType<SegmentSample>(),
        ];
    }

    private static SegmentSample? Sample(SegmentPiece piece, SegmentLabel? label, string station, string? series)
        => label is null ? null : new SegmentSample(piece.Features, station, series, label.Kind, label.Weight);
}
