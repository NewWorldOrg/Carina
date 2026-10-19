using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What a recording a round of learning could read is learned by: stretches the catalog hears an advert in, or
/// only other stretches of the catalog and what viewers did with its buttons.
/// </summary>
public enum LearningGround
{
    CatalogAdverts = 1,

    Other = 2,
}

/// <summary>
/// One recording a round of learning could read: when it started, how much of its learning data was read, and what
/// it is learned by.
/// </summary>
public sealed record LearningCandidate(RecordingId Recording, DateTime StartedAt, TimeSpan ReadThrough, LearningGround Ground);

/// <summary>
/// Which recordings one round of learning reads: no more than <see cref="MostAtOnce"/> of learning data, the
/// recordings the catalog hears an advert in first, then the rest, the most recently started first within each; a
/// recording that would go over what is left is passed over for the next that fits.
/// </summary>
public static class LearningSelection
{
    public static readonly TimeSpan MostAtOnce = TimeSpan.FromHours(120);

    public static IReadOnlyList<RecordingId> Choose(IReadOnlyList<LearningCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        List<RecordingId> chosen = [];
        TimeSpan left = MostAtOnce;

        foreach (LearningCandidate candidate in candidates
                     .OrderBy(candidate => candidate.Ground)
                     .ThenByDescending(candidate => candidate.StartedAt)
                     .ThenBy(candidate => candidate.Recording.Value)
                     .Where(candidate => candidate.ReadThrough > TimeSpan.Zero))
        {
            if (candidate.ReadThrough <= left)
            {
                chosen.Add(candidate.Recording);
                left -= candidate.ReadThrough;
            }
        }

        return chosen;
    }
}
