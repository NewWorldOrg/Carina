namespace Carina.Domain.Segments;

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
