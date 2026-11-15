using System.Runtime.Versioning;

using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

[SupportedOSPlatform("linux")]
internal static class ViewerSessions
{
    public static async Task<SessionSnapshot> WatchedSessionAsync(AppSwapFeature feature)
    {
        IReadOnlyList<SessionSnapshot> held = await Eventually.Yields(
            feature.App.SessionsAsync,
            sessions => Viewing(sessions) is not null,
            sessions => $"the driver holds {sessions.Count} session(s)",
            "the viewer is given a session of its own on the driver");
        SessionSnapshot? watching = Viewing(held);

        Assert.NotNull(watching);

        return watching;
    }

    public static SessionSnapshot? Viewing(IReadOnlyList<SessionSnapshot> sessions)
        => sessions.FirstOrDefault(session => session.Purpose is SessionPurpose.Live);
}
