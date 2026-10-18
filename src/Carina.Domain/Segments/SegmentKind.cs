namespace Carina.Domain.Segments;

/// <summary>
/// What a stretch of a recording is: the programme itself, a break for adverts, its opening or its
/// ending.
/// </summary>
public enum SegmentKind
{
    Programme = 1,

    Break = 2,

    Opening = 3,

    Ending = 4,
}
