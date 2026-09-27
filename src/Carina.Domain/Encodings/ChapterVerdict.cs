namespace Carina.Domain.Encodings;

/// <summary>
/// How a run answered where the breaks in a recording are.
/// </summary>
public enum ChapterVerdict
{
    NotAsked = 1,

    Marked = 2,

    NothingFound = 3,

    Discarded = 4,

    Unreadable = 5,
}
