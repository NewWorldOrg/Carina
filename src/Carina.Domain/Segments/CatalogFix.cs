namespace Carina.Domain.Segments;

/// <summary>
/// The kind the catalog fixes a segment to, and how likely that is: the confidence of the surest entry of that kind
/// heard over the segment, as the catalog estimated it (<see cref="CatalogKinds"/>).
/// </summary>
public sealed record CatalogFix
{
    public CatalogFix(SegmentKind kind, double chance)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A segment is fixed to one of the kinds of segment.");
        }

        if (!(chance is > 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(chance), chance, "A segment is fixed at a chance above 0, up to 1.");
        }

        Kind = kind;
        Chance = chance;
    }

    public SegmentKind Kind { get; }

    public double Chance { get; }
}
