namespace Carina.Domain.Segments;

/// <summary>
/// The version a round of learning works beside: the rollback made before it, if one was, and the version in use
/// after it.
/// </summary>
public sealed record SegmentVersionInUse(SegmentRollback? Rollback, SegmentModelVersion Active);

/// <summary>
/// A candidate kept: the candidate under its number, the numbers of the candidates before it let go, and what judging
/// it against the version in use says once the rollbacks of the last days are counted.
/// </summary>
public sealed record SegmentCandidateKept(SegmentModelVersion Candidate, IReadOnlyList<int> LetGo, AdoptionVerdict Verdict);

/// <summary>
/// Whether a candidate judged to take the place of the version in use did, and the version in use after the try.
/// </summary>
public sealed record SegmentCandidateAdoption(bool Adopted, SegmentModelVersion InUse);

/// <summary>
/// Moves the versions of the model as a round of learning decides, looking up the versions kept and what viewers did
/// with the buttons of each. Before learning, the version in use goes back to the one before it when
/// <see cref="SegmentRollback"/> says so. A candidate learned is kept under the next number, the candidates before it
/// that nothing names are let go (<see cref="SegmentModelVersion.IsLetGoBefore"/>), and it is judged again with the
/// rollbacks of the last days (<see cref="SegmentAdoption.AfterRollbacks"/>). A candidate judged to take the place of
/// the version in use does so only while the version in use is still the one it was judged against and was not chosen
/// by hand since.
/// </summary>
public sealed class SegmentVersionDomainService(
    ISegmentModelVersionRepository versions,
    ISkipEventRepository skips,
    TimeProvider clock)
{
    /// <summary>
    /// Rolls the version in use back when what viewers did with its buttons up to <paramref name="now"/> says so, and
    /// answers the version in use for the round.
    /// </summary>
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

    /// <summary>
    /// Keeps the candidate <paramref name="learned"/> made, lets go of the candidates before it that nothing names, and
    /// judges it again with the rollbacks of the last days.
    /// </summary>
    public async Task<SegmentCandidateKept> KeepCandidateAsync(SegmentLearned learned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(learned);

        SegmentModelVersion candidate = await versions.AddCandidateAsync(learned.Candidate, learned.Evaluation, Now(), cancellationToken);
        IReadOnlyList<int> letGo = await LetGoBeforeAsync(candidate.Number, cancellationToken);
        AdoptionVerdict verdict = SegmentAdoption.AfterRollbacks(learned.Verdict, await versions.ListAsync(cancellationToken), Now());

        return new SegmentCandidateKept(candidate, letGo, verdict);
    }

    /// <summary>
    /// Puts <paramref name="candidate"/> in use unless the version in use is no longer
    /// <paramref name="judgedAgainst"/>, or has been chosen by hand since.
    /// </summary>
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

    /// <summary>
    /// Lets go of the candidates learning made before the one numbered <paramref name="latest"/> that were never used
    /// and that nothing names, and answers their numbers, the lowest first.
    /// </summary>
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
