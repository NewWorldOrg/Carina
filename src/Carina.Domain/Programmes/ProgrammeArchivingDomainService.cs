namespace Carina.Domain.Programmes;

public sealed record ProgrammesArchived(int Kept, int Discarded);

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
