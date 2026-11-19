using Puppis.Contracts;

namespace Carina.Domain.Driver;

public interface ICardReaderStatusReader
{
    Task<DriverCall<CardReaderDto>> GetCardReaderAsync(CancellationToken cancellationToken);
}
