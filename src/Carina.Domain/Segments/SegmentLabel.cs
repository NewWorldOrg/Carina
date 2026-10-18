namespace Carina.Domain.Segments;

/// <summary>
/// Where a label a segment is learned from comes from: a person's answer, the catalog's kind, or what
/// viewers did with the skip buttons.
/// </summary>
public enum SegmentLabelSource
{
    Answer = 1,

    Catalog = 2,

    Viewer = 3,
}

/// <summary>
/// The kind one segment is learned as, the weight it is learned with, and where that came from.
/// </summary>
public sealed record SegmentLabel(SegmentKind Kind, double Weight, SegmentLabelSource Source);
