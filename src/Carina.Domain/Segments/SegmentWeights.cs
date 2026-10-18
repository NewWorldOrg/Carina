namespace Carina.Domain.Segments;

/// <summary>
/// The weights of the logistic regression a version of the model scores segments with, one set per
/// kind weighed against the programme. With two kinds, programme and break, only the break is weighed.
/// </summary>
public sealed class SegmentWeights : IEquatable<SegmentWeights>
{
    public SegmentWeights(IReadOnlyDictionary<SegmentKind, SegmentClassWeights> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        if (classes.Count is not 1 || !classes.ContainsKey(SegmentKind.Break))
        {
            throw new ArgumentException("Two kinds are told apart by the weights of the break against the programme.", nameof(classes));
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
