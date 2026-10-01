using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.TestSupport;

/// <summary>
/// The encode job ledger held in memory, holding one owner per artefact name under an output root.
/// </summary>
public sealed class HeldEncodeJobs : IEncodeJobRepository, IEncodeStandingReader
{
    public List<EncodeJob> Jobs { get; } = [];

    public List<string> Moves { get; } = [];

    public Action<EncodeJob, EncodeFileName>? WhenClaiming { get; set; }

    public Action<EncodeJob>? WhenSaving { get; set; }

    public Func<EncodeJob, bool>? WhenWritingTheEnding { get; set; }

    /// <summary>
    /// Whether a claim hands out a copy of the row, so that what is done to the job handed out reaches the ledger
    /// only when it is written back.
    /// </summary>
    public bool KeepsItsOwnRows { get; set; }

    /// <summary>
    /// The copy the last claim handed out while <see cref="KeepsItsOwnRows"/> was set.
    /// </summary>
    public EncodeJob? HandedOut { get; private set; }

    public Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        EncodeJob? row = Jobs.FirstOrDefault(job => job.Id.Equals(id));

        return Task.FromResult(KeepsItsOwnRows && row is not null ? Copied(row) : row);
    }

    public Task AddAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        Jobs.Add(job);
        Moves.Add($"added {job.Id.Wire}");

        return Task.CompletedTask;
    }

    public Task SaveAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        if (KeepsItsOwnRows)
        {
            WhenSaving?.Invoke(job);
            Land(job);
            Moves.Add($"saved {job.Id.Wire} {job.Status}");

            return Task.CompletedTask;
        }

        if (!Jobs.Contains(job))
        {
            Jobs.Add(job);
        }

        Moves.Add($"saved {job.Id.Wire} {job.Status}");
        WhenSaving?.Invoke(job);

        return Task.CompletedTask;
    }

    public Task<bool> WriteTheEndingAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        bool landed = WhenWritingTheEnding?.Invoke(job) ?? true;

        if (landed)
        {
            if (KeepsItsOwnRows)
            {
                Land(job);
            }

            Moves.Add($"wrote the ending {job.Id.Wire} {job.Status}");
        }

        return Task.FromResult(landed);
    }

    public Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        EncodeJob[] matched =
        [
            .. Jobs
                .Where(job => query.Statuses.Count is 0 || query.Statuses.Contains(job.Status))
                .Where(job => query.Recording is null || job.RecordingId.Equals(query.Recording))
                .OrderByDescending(job => job.QueuedAt)
                .ThenByDescending(job => job.Id.Value),
        ];

        return Task.FromResult(new PaginatedList<EncodeJob>(
            [.. matched.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            matched.Length,
            query.Page,
            query.PerPage));
    }

    public Task<EncodeStandingBoard> ReadAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        return Task.FromResult(EncodeStandingBoard.Of(
            Jobs
                .Where(job => recordings.Contains(job.RecordingId))
                .Select(job => (job.RecordingId, job.Status))));
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

        if (KeepsItsOwnRows)
        {
            HandedOut = Copied(next);

            return Task.FromResult(EncodeClaim.Of(HandedOut));
        }

        return Task.FromResult(EncodeClaim.Of(next));
    }

    private void Land(EncodeJob job)
    {
        int held = Jobs.FindIndex(row => row.Id.Equals(job.Id));

        if (held < 0)
        {
            Jobs.Add(Copied(job));
        }
        else
        {
            Jobs[held] = Copied(job);
        }
    }

    private static EncodeJob Copied(EncodeJob job)
        => EncodeJob.Rehydrate(
            job.Id,
            job.RecordingId,
            job.ProfileId,
            job.DestinationId,
            job.OutputRoot,
            job.Status,
            job.Attempt,
            job.QueuedAt,
            job.StartedAt,
            job.EndedAt,
            job.Failure,
            job.ArtefactName,
            job.Route,
            job.Programme,
            job.Headway,
            job.Timeline,
            job.Chapters,
            job.MakesItAgain,
            job.NameGivenUpAt,
            job.ReplacedAt);

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

        bool held = Jobs.Any(other => StillHolds(other, job, name));

        if (held)
        {
            Moves.Add($"refused {job.Id.Wire} {name.Value}");

            return Task.FromResult(ArtefactClaim.TakenByAnother);
        }

        job.Name(name);
        Moves.Add($"claimed {job.Id.Wire} {name.Value}");

        return Task.FromResult(ArtefactClaim.Claimed);
    }

    public Task<int> TakeTheNameOverAsync(
        EncodeJob job,
        EncodeFileName name,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(name);

        if (!job.MakesItAgain)
        {
            throw new InvalidOperationException(
                "Only a job a person asked to make the artefact again takes the name over from an earlier one.");
        }

        EncodeJob[] holders = [.. Jobs.Where(other => StillHolds(other, job, name))];

        foreach (EncodeJob holder in holders)
        {
            holder.GiveUpTheName(at);
            Moves.Add($"gave up {holder.Id.Wire} {name.Value}");
        }

        return Task.FromResult(holders.Length);
    }

    private static bool StillHolds(EncodeJob other, EncodeJob job, EncodeFileName name)
        => !other.Id.Equals(job.Id)
            && other.OutputRoot.Equals(job.OutputRoot)
            && name.Equals(other.ArtefactName)
            && other.NameGivenUpAt is null;

    public Task<IReadOnlyList<RecordingId>> ListRecordingsMadeMoreThanOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RecordingId> made =
        [
            .. Jobs
                .Where(job => job.Status is EncodeJobStatus.Completed)
                .GroupBy(job => job.RecordingId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        return Task.FromResult(made);
    }

    public Task<IReadOnlyList<EncodeSpell>> RecentSpellsAsync(int most, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeSpell> spells =
        [
            .. Jobs
                .Where(job => job.Status is EncodeJobStatus.Completed && job.StartedAt is not null && job.EndedAt is not null)
                .OrderByDescending(job => job.EndedAt)
                .Take(most)
                .Select(job => new EncodeSpell(job.EndedAt!.Value, job.EndedAt!.Value - job.StartedAt!.Value)),
        ];

        return Task.FromResult(spells);
    }

    public Task<EncodeHold> HoldOnProfileAsync(EncodeProfileId profileId, CancellationToken cancellationToken)
        => Task.FromResult(HeldBy(Jobs.Where(job => job.ProfileId.Equals(profileId))));

    public Task<EncodeHold> HoldOnDestinationAsync(EncodeDestinationId destinationId, CancellationToken cancellationToken)
        => Task.FromResult(HeldBy(Jobs.Where(job => job.DestinationId.Equals(destinationId))));

    private static EncodeHold HeldBy(IEnumerable<EncodeJob> named)
    {
        EncodeJob[] found = [.. named];

        return new EncodeHold(
            found.Length > 0,
            found
                .Where(job => job.Status is EncodeJobStatus.Running or EncodeJobStatus.Queued)
                .OrderBy(job => job.Status)
                .ThenBy(job => job.QueuedAt)
                .FirstOrDefault());
    }
}
