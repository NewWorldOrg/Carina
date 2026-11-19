namespace Carina.Domain.Segments;

public static class LeaveOneOut
{
    public static IReadOnlyList<SegmentSample> Samples(
        IReadOnlyList<IReadOnlyList<SegmentSample>> each,
        int heldOut,
        IReadOnlyDictionary<int, IReadOnlyList<SegmentSample>> readAgain)
    {
        ArgumentNullException.ThrowIfNull(each);
        ArgumentNullException.ThrowIfNull(readAgain);
        ArgumentOutOfRangeException.ThrowIfNegative(heldOut);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(heldOut, each.Count);

        if (readAgain.ContainsKey(heldOut) || readAgain.Keys.Any(at => at < 0 || at >= each.Count))
        {
            throw new ArgumentException("Only another recording of the round is read again.", nameof(readAgain));
        }

        return [.. each.SelectMany((samples, at) => at == heldOut ? [] : readAgain.GetValueOrDefault(at, samples))];
    }
}
