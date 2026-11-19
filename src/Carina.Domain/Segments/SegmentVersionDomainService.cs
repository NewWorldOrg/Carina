namespace Carina.Domain.Segments;

public sealed record SegmentVersionInUse(SegmentRollback? Rollback, SegmentModelVersion Active);

public sealed record SegmentCandidateKept(SegmentModelVersion Candidate, IReadOnlyList<int> LetGo, AdoptionVerdict Verdict);

public sealed record SegmentCandidateAdoption(bool Adopted, SegmentModelVersion InUse);

public sealed class SegmentVersionDomainService(
    ISegmentModelVersionRepository versions,
    ISkipEventRepository skips,
    TimeProvider clock)
{
    public async Task<SegmentVersionInUse> RollBackWhenDueAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (await RollbackAsync(now, cancellationToken) is not { } rollback)
        {
            return new SegmentVersionInUse(null, await versions.ActiveAsync(cancellationToken));
        }

        SegmentModelVersion active = await versions.ChangeAsync(SegmentVersionChange.GoingBack(rollback), Now(), cancellationToken)
                                     ?? throw new InvalidOperationException("The version gone back to is no longer kept.");

        return new SegmentVersionInUse(rollback, active);
    }

    public async Task<SegmentCandidateKept> KeepCandidateAsync(SegmentLearned learned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(learned);

        SegmentModelVersion candidate = await versions.AddCandidateAsync(learned.Candidate, learned.Evaluation, Now(), cancellationToken);
        IReadOnlyList<int> letGo = await LetGoBeforeAsync(candidate.Number, cancellationToken);
        AdoptionVerdict verdict = SegmentAdoption.AfterRollbacks(learned.Verdict, await versions.ListAsync(cancellationToken), Now());

        return new SegmentCandidateKept(candidate, letGo, verdict);
    }

    public async Task<SegmentCandidateAdoption> AdoptAsync(
        SegmentModelVersion candidate,
        SegmentModelVersion judgedAgainst,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(judgedAgainst);

        SegmentModelVersion inUse = await versions.ActiveAsync(cancellationToken);

        if (inUse.Number != judgedAgainst.Number || inUse.ByHand)
        {
            return new SegmentCandidateAdoption(false, inUse);
        }

        SegmentModelVersion adopted = await versions.ChangeAsync(SegmentVersionChange.Adopting(candidate.Number), Now(), cancellationToken)
                                      ?? throw new InvalidOperationException("The candidate just kept is no longer kept.");

        return new SegmentCandidateAdoption(true, adopted);
    }

    public async Task<IReadOnlyList<int>> LetGoBeforeAsync(int latest, CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentModelVersion> kept = await versions.ListAsync(cancellationToken);
        IReadOnlyList<int> before = SegmentModelVersion.LetGoBefore(kept, latest);

        if (before.Count is 0)
        {
            return [];
        }

        IReadOnlyList<int> letGo = SegmentModelVersion.LetGoBefore(kept, latest, await versions.NamedAmongAsync(before, cancellationToken));

        return letGo.Count is 0 ? [] : await versions.LetGoAsync(letGo, cancellationToken);
    }

    private async Task<SegmentRollback?> RollbackAsync(DateTime now, CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentModelVersion> kept = await versions.ListAsync(cancellationToken);
        SegmentModelVersion active = kept.Single(version => version.State is SegmentModelVersionState.Active);

        if (SegmentRollback.Previous(kept) is not { } previous || SegmentRollback.Spans(active, now) is not { } spans)
        {
            return null;
        }

        SkipReturns since = await skips.ReturnsAsync(active.Number, spans.From, spans.Until, cancellationToken);
        SkipReturns before = await skips.ReturnsAsync(previous.Number, spans.BeforeFrom, spans.BeforeUntil, cancellationToken);

        return SegmentRollback.Judge(active, previous, since, before, now);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
