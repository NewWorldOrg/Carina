namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastSettingsRepository
{
    Task TakeTurnAsync(CancellationToken cancellationToken);

    Task<DataBroadcastSettings?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(DataBroadcastSettings settings, CancellationToken cancellationToken);
}
