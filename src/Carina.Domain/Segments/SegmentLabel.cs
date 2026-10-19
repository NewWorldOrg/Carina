namespace Carina.Domain.Segments;

/// <summary>
/// Where a label a segment is learned from comes from: the catalog's kind, what viewers did with the skip
/// buttons, or no stretch of the catalog lying over it in a recording the catalog is sure of an advert in.
/// </summary>
public enum SegmentLabelSource
{
    Catalog = 2,

    Viewer = 3,

    Uncovered = 4,
}

/// <summary>
/// The kind one segment is learned as, the weight it is learned with, and where that came from.
/// </summary>
public sealed record SegmentLabel(SegmentKind Kind, double Weight, SegmentLabelSource Source);
