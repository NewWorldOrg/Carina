using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// What viewers did with the skip buttons, as a test sets it for each version: none until it is set. Every ask
/// is kept, with the version and the span asked about.
/// </summary>
public sealed class HeldSkipReturns : ISkipReturnsReader
{
    public Dictionary<int, SkipReturns> ByVersion { get; } = [];

    public List<(int ModelVersion, DateTime From, DateTime Until)> Asked { get; } = [];

    public Task<SkipReturns> ReadAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        Asked.Add((modelVersion, from, until));

        return Task.FromResult(ByVersion.GetValueOrDefault(modelVersion, SkipReturns.None));
    }
}
