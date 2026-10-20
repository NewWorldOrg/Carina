namespace Carina.Domain.Segments;

/// <summary>
/// What became of the day's round of learning. A round that stopped partway is taken again, so it is never kept as
/// finished; one that failed is, so that the next try is the next day's.
/// </summary>
public enum SegmentTrainingOutcome
{
    NothingToLearnFrom = 1,

    Kept = 2,

    Adopted = 3,

    Stopped = 4,

    InUseChangedMeanwhile = 5,

    NothingNew = 6,

    HeldAfterRollback = 7,

    Failed = 8,
}
