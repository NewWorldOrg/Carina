using Carina.Contracts;

namespace Carina.Domain.Recordings;

public static class RecordingSessions
{
    public const string Prefix = "rec-";

    public static SessionId Named(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return SessionId.Parse(Prefix + id.Wire);
    }
}
