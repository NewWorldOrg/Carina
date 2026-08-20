namespace Carina.Domain.Channels;

public static class CandidateOrder
{
    public static IComparer<CandidateChannel> ByWhatWasMeasured { get; } = new MeasuredFirst();

    public static CandidateChannel? Best(IEnumerable<CandidateChannel> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates.Order(ByWhatWasMeasured).FirstOrDefault();
    }

    public static CandidateChannel? BetterThanTheSelected(IEnumerable<CandidateChannel> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        CandidateChannel[] held = [.. candidates];

        if (held.FirstOrDefault(candidate => candidate.IsSelected) is not { } selected
            || Best(held) is not { } best)
        {
            return null;
        }

        return MeasuredFirst.ByMeasurement(best, selected) < 0 ? best : null;
    }

    private sealed class MeasuredFirst : IComparer<CandidateChannel>
    {
        public static int ByMeasurement(CandidateChannel x, CandidateChannel y)
        {
            int byLock = Locked(y).CompareTo(Locked(x));

            return byLock != 0
                ? byLock
                : Comparer<int?>.Default.Compare(Cnr(y), Cnr(x));
        }

        public int Compare(CandidateChannel? x, CandidateChannel? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            int byMeasurement = ByMeasurement(x, y);

            return byMeasurement != 0
                ? byMeasurement
                : x.Tuning.PhysicalChannel.CompareTo(y.Tuning.PhysicalChannel);
        }

        private static bool Locked(CandidateChannel candidate) => candidate.LastMeasurement?.Locked is true;

        private static int? Cnr(CandidateChannel candidate)
            => candidate.LastMeasurement is { Locked: true } measurement ? measurement.CnrMilliDecibels : null;
    }
}
