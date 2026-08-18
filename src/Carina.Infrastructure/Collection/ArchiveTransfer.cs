using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record Transferred(int Kept, int Discarded, int Forgotten);

public sealed class ArchiveTransfer(
    IProgrammeRepository programmes,
    IArchivedProgrammeRepository archive,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<ArchiveTransfer> logger)
{
    public const int MostPerRun = 5_000;

    public async Task<Transferred> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime ended = now - settings.KeepEndedProgrammes;
        IReadOnlyList<Programme> leaving = await programmes.ListEndedBeforeAsync(
            ended,
            MostPerRun,
            cancellationToken);
        ArchivedProgramme[] keeping =
        [
            .. leaving.Select(programme => ArchivedProgramme.Of(programme, now)).OfType<ArchivedProgramme>(),
        ];
        int kept = keeping.Length == 0 ? 0 : await archive.KeepAsync(keeping, cancellationToken);
        int discarded = await programmes.ForgetEndedBeforeAsync(ended, cancellationToken);
        int forgotten = settings.ArchiveRetention is { } retention
            ? await archive.ForgetBeforeAsync(now - retention, cancellationToken)
            : 0;

        if (kept > 0 || forgotten > 0)
        {
            logger.LogInformation(
                "The archive took {Kept} programme(s) and let go of {Forgotten}.",
                kept,
                forgotten);
        }

        return new Transferred(kept, discarded, forgotten);
    }
}
