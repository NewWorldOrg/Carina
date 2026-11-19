using Carina.Domain.Integrity;

namespace Carina.Infrastructure.Integrity;

public sealed class PlacedStrayFileEraser(
    IWrittenFileSurvey written,
    LocalStrayFileEraser local,
    DriverStrayFileEraser driver) : IStrayFileEraser
{
    public Task<StrayFileErasure> EraseAsync(IntegrityFinding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return written.Places.Contains(finding.Root)
            ? local.EraseAsync(finding, cancellationToken)
            : driver.EraseAsync(finding, cancellationToken);
    }
}
