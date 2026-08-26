namespace Carina.Domain.Channels;

public interface IServiceReachSettingsRepository
{
    Task<ServiceReachSettings> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ServiceReachSettings settings, CancellationToken cancellationToken);
}
