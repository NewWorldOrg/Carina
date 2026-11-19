namespace Carina.Domain.Segments;

public enum SegmentLabelSource
{
    Catalog = 2,

    Viewer = 3,

    Uncovered = 4,
}

public sealed record SegmentLabel(SegmentKind Kind, double Weight, SegmentLabelSource Source);
