namespace Carina.Domain.Segments;

/// <summary>
/// Where the versions of the model are kept. A version is removed from it only as learning lets go of the candidates
/// it did not take (<see cref="LetGoOfCandidatesBeforeAsync"/>), and a number once given is never given again.
/// </summary>
public interface ISegmentModelVersionRepository
{
    /// <summary>
    /// Every version kept, the highest number first.
    /// </summary>
    Task<IReadOnlyList<SegmentModelVersion>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="change"/> at <paramref name="at"/> as <see cref="SegmentModelVersion.Change"/>
    /// works it out, against the versions as they stand once no other change is being made, and writes what
    /// it moves all or nothing, the version leaving use first. Answers the version in use after it; none when
    /// the change names a number no version has. The only way the version in use changes beside keeping the built-in
    /// version.
    /// </summary>
    Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a candidate learning made at <paramref name="trainedAt"/>, numbered after the highest number given once
    /// no other change is being made (<see cref="SegmentModelVersion.NumberAfter"/>), and answers it. A number is
    /// given when a version is kept under it, and stays given when the version is let go: what the rounds of learning,
    /// the detections, the viewers' skips and the chapters of an encode remember of versions counts as given.
    /// </summary>
    Task<SegmentModelVersion> AddCandidateAsync(SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Lets go of the candidates learning did not take once it has kept the candidate numbered
    /// <paramref name="latest"/>, once no other change is being made: those
    /// <see cref="SegmentModelVersion.LetGoBefore"/> names that no detection, viewer's skip, chapters of an encode or
    /// round of learning measuring the version in use names. Answers the numbers let go, the lowest first; the
    /// numbers of the versions kept do not move.
    /// </summary>
    Task<IReadOnlyList<int>> LetGoOfCandidatesBeforeAsync(int latest, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps <paramref name="builtIn"/>, the built-in version of this release, at <paramref name="at"/> as
    /// <see cref="SegmentModelVersion.KeepBuiltIn"/> works it out, against the versions as they stand once no other
    /// change is being made and the highest number given as <see cref="AddCandidateAsync"/> counts it, and writes
    /// what it moves all or nothing, the version leaving use first. Answers what was kept; none when a built-in
    /// version already holds the same parameters.
    /// </summary>
    Task<SegmentBuiltInKept?> KeepBuiltInAsync(SegmentModel builtIn, DateTime at, CancellationToken cancellationToken);

    /// <summary>
    /// The version in use; there is always exactly one once the built-in version has been kept.
    /// </summary>
    Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The version numbered <paramref name="number"/>, in whatever state it is; none when no version has that number.
    /// </summary>
    Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken);
}
