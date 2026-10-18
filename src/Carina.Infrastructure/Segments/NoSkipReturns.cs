using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Answers no skips and no returns for any version: what viewers do with the skip buttons is not kept.
/// </summary>
public sealed class NoSkipReturns : ISkipReturnsReader
{
    public Task<SkipReturns> ReadAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken)
        => Task.FromResult(SkipReturns.None);
}
