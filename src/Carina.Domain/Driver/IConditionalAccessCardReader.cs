using Carina.Contracts;

namespace Carina.Domain.Driver;

/// <summary>
/// Asks the driver which conditional-access card answered it last. The answer names one card, so whoever asks keeps
/// it out of every log.
/// </summary>
public interface IConditionalAccessCardReader
{
    Task<DriverCall<ConditionalAccessCardDto>> GetConditionalAccessCardAsync(CancellationToken cancellationToken);
}
