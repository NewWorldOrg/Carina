using Puppis.Contracts;

namespace Carina.Domain.Driver;

public interface IConditionalAccessCardReader
{
    Task<DriverCall<ConditionalAccessCardDto>> GetConditionalAccessCardAsync(CancellationToken cancellationToken);
}
