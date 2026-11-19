namespace Carina.Domain.Segments;

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
