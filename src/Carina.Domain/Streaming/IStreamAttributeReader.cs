using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface IStreamAttributeReader
{
    Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken);

    Task<CarriedSounds> SoundsAsync(StreamSource source, ServiceId service, CancellationToken cancellationToken);
}
