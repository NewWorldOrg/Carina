namespace Carina.Domain.Segments;

public sealed record JoinedPiece
{
    public JoinedPiece(int first, int last, SegmentPiece piece, IReadOnlyList<SegmentKind> kinds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(kinds);

        if (last <= first)
        {
            throw new ArgumentException("A joined piece takes in two segments or more.", nameof(last));
        }

        if (kinds.Count is 0 || kinds.Any(kind => kind is SegmentKind.Programme || !Enum.IsDefined(kind)))
        {
            throw new ArgumentException("A joined piece is taken for a break, an opening or an ending.", nameof(kinds));
        }

        First = first;
        Last = last;
        Piece = piece;
        Kinds = [.. kinds.Distinct()];
    }

    public int First { get; }

    public int Last { get; }

    public SegmentPiece Piece { get; }

    public IReadOnlyList<SegmentKind> Kinds { get; }
}
