using Carina.Api.Common;
using Carina.Domain.DriverStatus;

namespace Carina.Api.Services;

public sealed class DriverStatusService(
    IDriverStatusReader driverStatusReader,
    TimeProvider timeProvider,
    ILogger<DriverStatusService> logger)
{
    public async Task<ServiceResult<DriverStatusSnapshot>> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            DriverObservation observation = await driverStatusReader.ReadAsync(cancellationToken);

            return ServiceResult<DriverStatusSnapshot>.Success(
                DriverStatusSnapshot.Observe(observation, timeProvider));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Reading the driver status failed.");

            return ServiceResult<DriverStatusSnapshot>.Failure("The driver status is unavailable.");
        }
    }
}
