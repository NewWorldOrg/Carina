namespace Carina.Domain.Encodings;

/// <summary>
/// How a run answered the question of where the breaks in a recording are. "Nobody looked" and
/// "somebody looked and there was nothing to mark" are different answers, and so are "the reading
/// was thrown away because it could not be believed" and "the source could not be read at all".
/// </summary>
public enum ChapterVerdict
{
    NotAsked = 1,

    Marked = 2,

    NothingFound = 3,

    Discarded = 4,

    Unreadable = 5,
}
