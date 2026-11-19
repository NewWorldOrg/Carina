using Puppis.Contracts;

namespace Carina.Domain.Recordings;

public interface IRecordingTitles
{
    Task<IReadOnlyDictionary<SessionId, string>> TitlesAsync(CancellationToken cancellationToken);
}
