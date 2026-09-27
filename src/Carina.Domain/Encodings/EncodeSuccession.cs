using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// Which job's artefact is a recording's one, and which completed jobs of it made an artefact that
/// the newer one replaces. The artefact of the job that completed last stands.
/// </summary>
public sealed record EncodeSuccession
{
    private EncodeSuccession(EncodeJob? standing, IReadOnlyList<EncodeJob> toReplace)
    {
        Standing = standing;
        ToReplace = toReplace;
    }

    public EncodeJob? Standing { get; }

    public IReadOnlyList<EncodeJob> ToReplace { get; }

    public static EncodeSuccession Of(IReadOnlyList<EncodeJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        RecordingId[] recordings = [.. jobs.Select(job => job.RecordingId).Distinct()];

        if (recordings.Length > 1)
        {
            throw new ArgumentException("A succession is worked out among the jobs of one recording.", nameof(jobs));
        }

        EncodeJob[] standing =
        [
            .. jobs
                .Where(job => job.StandsAsTheArtefact)
                .OrderByDescending(job => job.EndedAt)
                .ThenByDescending(job => job.QueuedAt),
        ];

        return standing.Length is 0
            ? new EncodeSuccession(null, [])
            : new EncodeSuccession(standing[0], [.. standing.Skip(1)]);
    }
}
