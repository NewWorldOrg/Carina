namespace Carina.Domain.Programmes;

public sealed record ProgrammeFeedSettings
{
    public int ConcurrentReaders { get; init; } = 4;

    public TimeSpan StatementTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
