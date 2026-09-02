using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Primitives;

namespace Carina.Api.Live;

public static class LiveWireRequest
{
    public const string Network = "network";

    public const string Service = "service";

    public const string Profile = "profile";

    public static readonly string TheKeyThereIs =
        $"A wire is asked for by `{Network}` and `{Service}` as whole numbers and `{Profile}` as one of "
        + $"{string.Join(", ", LiveProfile.All.Select(profile => profile.Name))}, each said once.";

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
