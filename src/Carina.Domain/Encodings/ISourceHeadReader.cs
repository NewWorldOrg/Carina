using Carina.Domain.Channels;

namespace Carina.Domain.Encodings;

public interface ISourceHeadReader
{
    Task<SourceHeadReading> ReadAsync(string source, ServiceId service, CancellationToken cancellationToken);
}
