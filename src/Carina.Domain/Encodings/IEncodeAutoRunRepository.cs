namespace Carina.Domain.Encodings;

public interface IEncodeAutoRunRepository
{
    Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken);
}

/// <summary>
/// How the queue runs, read as one answer whether or not anybody has settled it. A change made from
/// a screen is in force on the next read.
/// </summary>
public interface IEncodeAutoRunReader
{
    Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken);
}
