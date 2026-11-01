using Carina.Contracts;

namespace Carina.Domain.Recordings;

/// <summary>
/// The programme name of every recording being written, by the session the driver writes it in.
/// </summary>
public interface IRecordingTitles
{
    Task<IReadOnlyDictionary<SessionId, string>> TitlesAsync(CancellationToken cancellationToken);
}
