using Carina.Api.Common;
using Carina.Domain.DriverStatus;

namespace Carina.Api.Services;

public sealed class DriverStatusService(IDriverStatusReader driverStatusReader, TimeProvider timeProvider)
{
    public async Task<ServiceResult<DriverStatusSnapshot>> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var observation = await driverStatusReader.ReadAsync(cancellationToken);

            return ServiceResult<DriverStatusSnapshot>.Success(
                DriverStatusSnapshot.Observe(observation, timeProvider));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return ServiceResult<DriverStatusSnapshot>.Failure(
                $"The driver status reader failed: {error.Message}");
        }
    }
}
