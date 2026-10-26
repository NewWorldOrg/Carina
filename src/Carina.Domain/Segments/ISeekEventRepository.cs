using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the seeks viewers made are kept. Nothing is removed from it, and a recording thrown away leaves its seeks
/// where they are.
/// </summary>
public interface ISeekEventRepository
{
    /// <summary>
    /// Keeps <paramref name="seek"/> unless its recording already has <see cref="SeekEvent.MostAnHour"/> seeks that
    /// happened within <see cref="SeekEvent.Within"/> before it, and says whether it was kept.
    /// </summary>
    Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken);

    /// <summary>
    /// The seeks kept for the recordings named, the earliest that happened first.
    /// </summary>
    Task<IReadOnlyList<SeekEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);
}
