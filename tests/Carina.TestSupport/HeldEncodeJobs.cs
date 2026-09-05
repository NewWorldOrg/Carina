using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.TestSupport;

/// <summary>
/// The encode job ledger held in memory, with the one rule the real one gets from its index: one
/// owner per artefact name under an output root.
/// </summary>
public sealed class HeldEncodeJobs : IEncodeJobRepository
{
    public List<EncodeJob> Jobs { get; } = [];

    public List<string> Moves { get; } = [];

    public Action<EncodeJob, EncodeFileName>? WhenClaiming { get; set; }

    public Action<EncodeJob>? WhenSaving { get; set; }

    public Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken)
        => Task.FromResult(Jobs.FirstOrDefault(job => job.Id.Equals(id)));

    public Task AddAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        Jobs.Add(job);
        Moves.Add($"added {job.Id.Wire}");

        return Task.CompletedTask;
    }

    public Task SaveAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        if (!Jobs.Contains(job))
        {
            Jobs.Add(job);
        }

        Moves.Add($"saved {job.Id.Wire} {job.Status}");
        WhenSaving?.Invoke(job);

        return Task.CompletedTask;
    }

    public Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        EncodeJob[] matched =
        [
            .. Jobs
                .Where(job => query.Statuses.Count is 0 || query.Statuses.Contains(job.Status))
                .OrderByDescending(job => job.QueuedAt)
                .ThenByDescending(job => job.Id.Value),
        ];

        return Task.FromResult(new PaginatedList<EncodeJob>(
            [.. matched.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            matched.Length,
            query.Page,
            query.PerPage));
    }

    public Task<IReadOnlyList<EncodeJob>> ListForRecordingAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeJob> listed = [.. Jobs.Where(job => job.RecordingId.Equals(recordingId)).OrderBy(job => job.QueuedAt)];

        return Task.FromResult(listed);
    }

    public Task<EncodeClaim> ClaimNextAsync(DateTime at, CancellationToken cancellationToken)
    {
        if (Jobs.Any(job => job.Status is EncodeJobStatus.Running))
        {
            Moves.Add("claim refused: another is running");

            return Task.FromResult(EncodeClaim.AnotherIsRunning());
        }

        EncodeJob? next = Jobs
            .Where(job => job.Status is EncodeJobStatus.Queued)
            .OrderBy(job => job.QueuedAt)
            .FirstOrDefault();

        if (next is null)
        {
            Moves.Add("claim found nothing waiting");

            return Task.FromResult(EncodeClaim.NothingWaiting());
        }

        next.Start(at);
        Moves.Add($"claimed {next.Id.Wire} to run");

        return Task.FromResult(EncodeClaim.Of(next));
    }

    public Task<IReadOnlyList<EncodeJob>> ListRunningAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeJob> running = [.. Jobs.Where(job => job.Status is EncodeJobStatus.Running).OrderBy(job => job.StartedAt)];

        return Task.FromResult(running);
    }

    public Task<ArtefactClaim> ClaimArtefactAsync(EncodeJob job, EncodeFileName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(name);

        WhenClaiming?.Invoke(job, name);

        if (job.Status is not EncodeJobStatus.Running)
        {
            throw new InvalidOperationException("Only a running job names its artefact.");
        }

        bool held = Jobs.Any(other =>
            !other.Id.Equals(job.Id) && other.OutputRoot.Equals(job.OutputRoot) && name.Equals(other.ArtefactName));

        if (held)
        {
            Moves.Add($"refused {job.Id.Wire} {name.Value}");

            return Task.FromResult(ArtefactClaim.TakenByAnother);
        }

        job.Name(name);
        Moves.Add($"claimed {job.Id.Wire} {name.Value}");

        return Task.FromResult(ArtefactClaim.Claimed);
    }
}
