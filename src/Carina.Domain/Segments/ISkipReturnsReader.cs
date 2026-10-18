namespace Carina.Domain.Segments;

/// <summary>
/// Reads what viewers did with the skip buttons one version's detection offered.
/// </summary>
public interface ISkipReturnsReader
{
    /// <summary>
    /// The skips and returns on the buttons of the version numbered <paramref name="modelVersion"/> taken in
    /// from <paramref name="from"/> up to and including <paramref name="until"/>, as
    /// <see cref="SkipReturns.Of"/> counts them.
    /// </summary>
    Task<SkipReturns> ReadAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken);
}
