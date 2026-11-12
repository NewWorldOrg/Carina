using Carina.Domain.Base;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record Transferred(int Kept, int Discarded, int Forgotten);

public sealed class ArchiveTransfer(
    ProgrammeArchivingDomainService archiving,
    IAtomicWrite writes,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<ArchiveTransfer> logger)
{
    public async Task<Transferred> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<EndedProgramme> leaving = await archiving.ListLeavingAsync(settings, now, cancellationToken);
        Transferred moved = leaving.Count == 0
            ? new Transferred(0, 0, 0)
            : await writes.AllOrNothingAsync(
                async token =>
                {
                    ProgrammesArchived archived = await archiving.ArchiveAsync(leaving, now, token);

                    return new Transferred(archived.Kept, archived.Discarded, 0);
                },
                cancellationToken);
        int forgotten = await archiving.ForgetExpiredAsync(settings, now, cancellationToken);

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
