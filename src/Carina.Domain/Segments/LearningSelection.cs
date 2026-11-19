using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public enum LearningGround
{
    CatalogAdverts = 1,

    Other = 2,
}

public sealed record LearningCandidate(RecordingId Recording, DateTime StartedAt, TimeSpan ReadThrough, LearningGround Ground);

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
