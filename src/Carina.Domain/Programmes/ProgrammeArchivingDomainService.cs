namespace Carina.Domain.Programmes;

public sealed record ProgrammesArchived(int Kept, int Discarded);

/// <summary>
/// Decides what leaves the guide for the archive and what the archive lets go of. A programme leaves once it has
/// been over for longer than the guide keeps ended programmes, at most <see cref="MostPerRun"/> in one run; a shadow
/// leaves without being archived. The archive lets go of what ended before its retention, and of nothing when no
/// retention is set.
/// </summary>
public sealed class ProgrammeArchivingDomainService(
    IProgrammeRepository programmes,
    IArchivedProgrammeRepository archive)
{
    public const int MostPerRun = 5_000;

    public Task<IReadOnlyList<EndedProgramme>> ListLeavingAsync(
        CollectionSettings settings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return programmes.ListEndedBeforeAsync(now - settings.KeepEndedProgrammes, MostPerRun, cancellationToken);
    }

    /// <summary>
    /// Archives what can be archived of <paramref name="leaving"/> first, and then takes every one of them out of the
    /// guide, so that a programme the archive refused is never taken out.
    /// </summary>
    public async Task<ProgrammesArchived> ArchiveAsync(
        IReadOnlyList<EndedProgramme> leaving,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leaving);

        ArchivedProgramme[] keeping =
        [
            .. leaving.Select(ended => ArchivedProgramme.Of(ended, now)).OfType<ArchivedProgramme>(),
        ];
        int kept = await archive.KeepAsync(keeping, cancellationToken);
        int discarded = await programmes.ForgetAsync([.. leaving.Select(ended => ended.Programme)], cancellationToken);

        return new ProgrammesArchived(kept, discarded);
    }

    public async Task<int> ForgetExpiredAsync(
        CollectionSettings settings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ArchiveRetention is { } retention
            ? await archive.ForgetBeforeAsync(now - retention, cancellationToken)
            : 0;
    }
}
