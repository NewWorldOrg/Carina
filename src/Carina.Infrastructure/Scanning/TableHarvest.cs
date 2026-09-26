using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Infrastructure.Scanning;

public sealed class TableHarvest
{
    private readonly SectionReader reader = new(NetworkInformationTable.Pid, ServiceDescriptionTable.Pid);
    private readonly Dictionary<int, SectionSet> networks = [];
    private readonly Dictionary<int, SectionSet> descriptions = [];

    public long Bytes { get; private set; }

    public long RejectedSections { get; private set; }

    public long MalformedTables { get; private set; }

    public long UnreadablePackets => reader.UnreadablePackets;

    public HarvestedNetwork? Network { get; private set; }

    public HarvestedDescription? Description { get; private set; }

    public bool IsComplete => Network is not null && Description is not null;

    private readonly byte[] carry = new byte[TransportPacket.Size];

    private int carried;

    public void Push(ReadOnlySpan<byte> bytes)
    {
        Bytes += bytes.Length;

        if (carried > 0)
        {
            int take = Math.Min(TransportPacket.Size - carried, bytes.Length);

            bytes[..take].CopyTo(carry.AsSpan(carried));
            carried += take;
            bytes = bytes[take..];

            if (carried < TransportPacket.Size)
            {
                return;
            }

            Read(carry);
            carried = 0;
        }

        int whole = bytes.Length - (bytes.Length % TransportPacket.Size);

        Read(bytes[..whole]);
        bytes[whole..].CopyTo(carry);
        carried = bytes.Length - whole;
    }

    private void Read(ReadOnlySpan<byte> packets)
    {
        foreach (SectionRead read in reader.Push(packets))
        {
            switch (read)
            {
                case SectionRead.Assembled assembled:
                    Take(assembled.Section);

                    break;

                case SectionRead.Rejected:
                    RejectedSections++;

                    break;
            }
        }
    }

    public string Describe()
    {
        var missing = new List<string>();

        if (Network is null)
        {
            missing.Add("the network information table");
        }

        if (Description is null)
        {
            missing.Add("the service description table");
        }

        return $"{string.Join(" and ", missing)} never completed over {Bytes} bytes"
            + $" ({RejectedSections} sections rejected, {MalformedTables} tables malformed,"
            + $" {UnreadablePackets} packets unreadable)";
    }

    private void Take(Section section)
    {
        if (section.TableId == NetworkInformationTable.ActualNetworkTableId)
        {
            Collect(networks, section, CompleteNetwork);
        }
        else if (section.TableId == ServiceDescriptionTable.ActualStreamTableId)
        {
            Collect(descriptions, section, CompleteDescription);
        }
    }

    private static void Collect(
        Dictionary<int, SectionSet> sets,
        Section section,
        Action<IReadOnlyList<Section>> whenComplete)
    {
        if (!sets.TryGetValue(section.TableIdExtension, out SectionSet? set))
        {
            set = new SectionSet(section.TableId, section.TableIdExtension);
            sets[section.TableIdExtension] = set;
        }

        if (set.Add(section) && set.TryComplete(out IReadOnlyList<Section>? sections))
        {
            whenComplete(sections);
        }
    }

    private void CompleteNetwork(IReadOnlyList<Section> sections)
    {
        var tables = new List<NetworkInformationTable>(sections.Count);

        foreach (Section section in sections)
        {
            if (NetworkInformationTable.Read(section) is not TableRead<NetworkInformationTable>.Parsed parsed)
            {
                MalformedTables++;

                return;
            }

            tables.Add(parsed.Table);
        }

        Network = new HarvestedNetwork(
            tables[0].NetworkId,
            tables.Select(table => table.NetworkName).FirstOrDefault(name => name.Length > 0) ?? string.Empty,
            [.. tables.SelectMany(table => table.TransportStreams)]);
    }

    private void CompleteDescription(IReadOnlyList<Section> sections)
    {
        var tables = new List<ServiceDescriptionTable>(sections.Count);

        foreach (Section section in sections)
        {
            if (ServiceDescriptionTable.Read(section) is not TableRead<ServiceDescriptionTable>.Parsed parsed)
            {
                MalformedTables++;

                return;
            }

            tables.Add(parsed.Table);
        }

        Description = new HarvestedDescription(
            tables[0].TransportStreamId,
            tables[0].OriginalNetworkId,
            [.. tables.SelectMany(table => table.Services)]);
    }
}
