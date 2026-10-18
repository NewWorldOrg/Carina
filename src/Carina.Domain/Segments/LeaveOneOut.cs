namespace Carina.Domain.Segments;

/// <summary>
/// The samples one fold of the time-ordered leave-one-out learns from: every recording's samples but those of the
/// recording held out, in the order the recordings were handed over, a recording read again without the held-out
/// answer giving the samples read again in place of its own. The held-out recording's labels are masked rather than
/// every other recording's readings copied: the samples of a recording not read again are the same ones the whole
/// round learns from.
/// </summary>
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
