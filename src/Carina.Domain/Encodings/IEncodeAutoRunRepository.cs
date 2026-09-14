namespace Carina.Domain.Encodings;

public interface IEncodeAutoRunRepository
{
    Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken);
}

/// <summary>
/// How the queue runs, read as one answer whether or not anybody has settled it. Everything that
/// acts on these values asks through here rather than through the deployed settings, so a change
/// made from a screen is in force on the next look without the process being restarted.
/// </summary>
public interface IEncodeAutoRunReader
{
    Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken);
}
