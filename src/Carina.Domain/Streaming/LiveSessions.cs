using Carina.Contracts;

namespace Carina.Domain.Streaming;

public static class LiveSessions
{
    public const string Prefix = "live-";

    public static SessionId Fresh() => SessionId.Parse($"{Prefix}{Guid.NewGuid():n}");
}
