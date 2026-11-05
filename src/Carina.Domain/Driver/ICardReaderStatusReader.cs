using Puppis.Contracts;

namespace Carina.Domain.Driver;

/// <summary>
/// Asks the driver how its card reader stands. The answer names no card.
/// </summary>
public interface ICardReaderStatusReader
{
    Task<DriverCall<CardReaderDto>> GetCardReaderAsync(CancellationToken cancellationToken);
}
