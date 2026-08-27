using System.Globalization;

using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public static class ProgrammeServiceText
{
    public static IReadOnlyList<ProgrammeService>? Every(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return [];
        }

        var read = new List<ProgrammeService>(names.Count);

        foreach (string name in names)
        {
            if (Read(name) is not { } service)
            {
                return null;
            }

            read.Add(service);
        }

        return read;
    }

    public static ProgrammeService? Read(string? text)
    {
        string[] parts = (text ?? string.Empty).Split('-');

        if (parts.Length != 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out int network)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int service))
        {
            return null;
        }

        return network is < NetworkId.MinValue or > NetworkId.MaxValue
            || service is < ServiceId.MinValue or > ServiceId.MaxValue
                ? null
                : new ProgrammeService(network, service);
    }
}
