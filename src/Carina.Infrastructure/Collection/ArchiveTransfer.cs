using Carina.Domain.Base;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record Transferred(int Kept, int Discarded, int Forgotten);

public sealed class ArchiveTransfer(
    IProgrammeRepository programmes,
    IArchivedProgrammeRepository archive,
    IAtomicWrite writes,
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
        Transferred moved = leaving.Count == 0
            ? new Transferred(0, 0, 0)
            : await writes.AllOrNothingAsync(
                async token => new Transferred(
                    await archive.KeepAsync(keeping, token),
                    await programmes.ForgetAsync(leaving, token),
                    0),
                cancellationToken);
        int forgotten = settings.ArchiveRetention is { } retention
            ? await archive.ForgetBeforeAsync(now - retention, cancellationToken)
            : 0;

        if (moved.Kept > 0 || forgotten > 0)
        {
            logger.LogInformation(
                "The archive took {Kept} programme(s) and let go of {Forgotten}.",
                moved.Kept,
                forgotten);
        }

        return moved with { Forgotten = forgotten };
    }
}
