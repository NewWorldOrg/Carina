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
}
