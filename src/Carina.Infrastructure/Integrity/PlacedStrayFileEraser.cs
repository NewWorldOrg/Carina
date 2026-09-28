using Carina.Domain.Integrity;

namespace Carina.Infrastructure.Integrity;

/// <summary>
/// Hands a file no ledger claims to the process that wrote the place it is in: this process for the
/// places it writes into itself, and the driver for the recording roots.
/// </summary>
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
