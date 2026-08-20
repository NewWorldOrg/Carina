namespace Carina.Domain.Channels;

public static class CandidateOrder
{
    public static IComparer<CandidateChannel> ByWhatWasMeasured { get; } = new MeasuredFirst();

    public static CandidateChannel? Best(IEnumerable<CandidateChannel> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates.Order(ByWhatWasMeasured).FirstOrDefault();
    }

    private sealed class MeasuredFirst : IComparer<CandidateChannel>
    {
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

            int byLock = Locked(y).CompareTo(Locked(x));

            if (byLock != 0)
            {
                return byLock;
            }

            int byCnr = Comparer<int?>.Default.Compare(Cnr(y), Cnr(x));

            return byCnr != 0
                ? byCnr
                : x.Tuning.PhysicalChannel.CompareTo(y.Tuning.PhysicalChannel);
        }

        private static bool Locked(CandidateChannel candidate) => candidate.LastMeasurement?.Locked is true;

        private static int? Cnr(CandidateChannel candidate)
            => candidate.LastMeasurement is { Locked: true } measurement ? measurement.CnrMilliDecibels : null;
    }
}
