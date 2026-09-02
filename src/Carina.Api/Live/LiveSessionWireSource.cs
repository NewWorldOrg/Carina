using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Primitives;

namespace Carina.Api.Live;

public sealed class LiveSessionWireSource(IHttpContextAccessor requests, ILiveSessionManager sessions) : ILiveWireSource
{
    public const string Network = "network";

    public const string Service = "service";

    public const string Profile = "profile";

    public async ValueTask<ILiveViewing?> JoinAsync(CancellationToken cancellationToken)
    {
        if (requests.HttpContext is not { } context || KeyOf(context.Request.Query) is not { } key)
        {
            return null;
        }

        LiveJoin join = await sessions.JoinAsync(key, cancellationToken);

        return join.Viewing;
    }

    public static LiveSessionKey? KeyOf(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Numbered(query[Network], NetworkId.MinValue, NetworkId.MaxValue, out int network)
            || !Numbered(query[Service], ServiceId.MinValue, ServiceId.MaxValue, out int service)
            || query[Profile] is not { Count: 1 } named
            || LiveProfile.Find(named[0]) is not { } profile)
        {
            return null;
        }

        return new LiveSessionKey(new NetworkId(network), new ServiceId(service), profile);
    }

    private static bool Numbered(StringValues said, int lowest, int highest, out int number)
    {
        number = 0;

        return said.Count is 1
               && int.TryParse(said[0], NumberStyles.None, CultureInfo.InvariantCulture, out number)
               && number >= lowest
               && number <= highest;
    }
}
