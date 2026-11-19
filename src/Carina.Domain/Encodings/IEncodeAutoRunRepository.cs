namespace Carina.Domain.Encodings;

public interface IEncodeAutoRunRepository
{
    Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken);
}

public interface IEncodeAutoRunReader
{
    Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken);
}
