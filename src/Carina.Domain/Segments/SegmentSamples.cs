namespace Carina.Domain.Segments;

/// <summary>
/// The samples one recording's segments are learned from: each segment, and each run of neighbouring segments read as
/// one (<see cref="JoinedPiece"/>), that <see cref="SegmentLabels"/> labels, with the features detection scores it by,
/// so that a run is learned as the sequence takes it in place of its segments. A segment inside a run keeps its own
/// label beside the run's.
/// </summary>
public static class SegmentSamples
{
    /// <summary>
    /// The samples of <paramref name="read"/> by what <paramref name="context"/> hears of the catalog and what viewers
    /// did with the buttons and by seeking; a segment is touched by the catalog as <see cref="SegmentLabels.Of"/> says, by <paramref name="touching"/>
    /// when it is given.
    /// </summary>
    public static IReadOnlyList<SegmentSample> Of(
        RecordingPieces read,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<CatalogHeard>? touching = null,
        ViewerSeeks? seeks = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        string? series = read.Series is string named && SegmentModel.IsASeries(named) ? named : null;
        IReadOnlyList<SegmentLabel?> pieces = SegmentLabels.Of(read.Pieces, context, actions, touching, seeks);
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
