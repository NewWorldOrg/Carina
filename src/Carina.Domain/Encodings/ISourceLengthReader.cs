namespace Carina.Domain.Encodings;

public interface ISourceLengthReader
{
    Task<SourceLengthReading> ReadAsync(string source, CancellationToken cancellationToken);
}
