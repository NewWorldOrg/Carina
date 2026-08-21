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
        (Programme Leaving, ArchivedProgramme? Kept)[] offered =
        [
            .. leaving.Select(programme => (programme, ArchivedProgramme.Of(programme, now))),
        ];
        ArchivedProgramme[] keeping = [.. offered.Select(pair => pair.Kept).OfType<ArchivedProgramme>()];
        Programme[] discarding =
        [
            .. offered
                .Where(pair => pair.Kept is not null || pair.Leaving.IsShadow)
                .Select(pair => pair.Leaving),
        ];
        Transferred moved = leaving.Count == 0
            ? new Transferred(0, 0, 0)
            : await writes.AllOrNothingAsync(
                async token => new Transferred(
                    await archive.KeepAsync(keeping, token),
                    await programmes.ForgetAsync(discarding, token),
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
