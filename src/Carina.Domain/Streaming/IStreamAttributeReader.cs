namespace Carina.Domain.Streaming;

public interface IStreamAttributeReader
{
    Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken);
}
