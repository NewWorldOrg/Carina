namespace Carina.Domain.Segments;

/// <summary>
/// Where the skip events are kept. Nothing is removed from it, and a recording thrown away leaves its events
/// where they are.
/// </summary>
public interface ISkipEventRepository
{
    /// <summary>
    /// Keeps <paramref name="skip"/> unless its recording already has <see cref="SkipEvent.MostAnHour"/> events
    /// that happened within <see cref="SkipEvent.Within"/> before it, and says whether it was kept.
    /// </summary>
    Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken);

    /// <summary>
    /// Every event kept, the earliest that happened first.
    /// </summary>
    Task<IReadOnlyList<SkipEvent>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The skips and returns on the buttons of the version numbered <paramref name="modelVersion"/> that happened
    /// from <paramref name="from"/> up to and including <paramref name="until"/>, as <see cref="SkipReturns.Of"/>
    /// counts them.
    /// </summary>
    Task<SkipReturns> ReturnsAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken);
}
