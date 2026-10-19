namespace Carina.Domain.Segments;

/// <summary>
/// The weights of the logistic regression a version of the model scores segments with, one set per
/// kind weighed against the programme. With two kinds, programme and break, only the break is weighed;
/// with four, the break, the opening and the ending each are.
/// </summary>
public sealed class SegmentWeights : IEquatable<SegmentWeights>
{
    private static readonly SegmentKind[] TwoKinds = [SegmentKind.Break];

    private static readonly SegmentKind[] FourKinds = [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending];

    public SegmentWeights(IReadOnlyDictionary<SegmentKind, SegmentClassWeights> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        SegmentKind[] weighed = [.. classes.Keys.Order()];

        if (!weighed.SequenceEqual(TwoKinds) && !weighed.SequenceEqual(FourKinds))
        {
            throw new ArgumentException(
                "Two kinds are told apart by the weights of the break against the programme, and four by those of the break, the opening and the ending.",
                nameof(classes));
        }

        if (classes.Values.Any(weights => weights is null))
        {
            throw new ArgumentException("Every kind weighed carries its weights.", nameof(classes));
        }

        Classes = SortedMaps.Copied(classes, Comparer<SegmentKind>.Default);
    }

    public IReadOnlyDictionary<SegmentKind, SegmentClassWeights> Classes { get; }

    public bool Equals(SegmentWeights? other) => other is not null && SortedMaps.Same(Classes, other.Classes);

    public override bool Equals(object? obj) => Equals(obj as SegmentWeights);

    public override int GetHashCode() => SortedMaps.Hash(Classes);
}
