namespace Carina.Domain.Channels;

public interface ITunerCapacityDirectory
{
    Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken);
}
