namespace Carina.Domain.Segments;

/// <summary>
/// Where taking the learning data out of a recording stands.
/// </summary>
public enum LearningExtractionState
{
    Following = 1,

    Waiting = 2,

    Reading = 3,

    Done = 4,

    Partial = 5,

    Failed = 6,
}
