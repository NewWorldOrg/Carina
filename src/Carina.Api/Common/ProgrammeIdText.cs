using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Api.Common;

public static class ProgrammeIdText
{
    public static string Of(ProgrammeId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{id.NetworkId.Value}-{id.ServiceId.Value}-{id.EventId.Value}");
    }

    public static ProgrammeId? Read(string? text)
    {
        string[] parts = (text ?? string.Empty).Split('-');

        if (parts.Length != 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out int network)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int service)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out int carried))
        {
            return null;
        }

        try
        {
            return new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
