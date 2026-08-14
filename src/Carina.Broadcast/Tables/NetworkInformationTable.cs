using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

public sealed class NetworkInformationTable
{
    public const int Pid = 0x0010;

    public const int ActualNetworkTableId = 0x40;

    public const int OtherNetworkTableId = 0x41;

    private const int TransportStreamHeaderSize = 6;

    private NetworkInformationTable(
        Section section,
        IReadOnlyList<Descriptor> networkDescriptors,
        IReadOnlyList<NetworkTransportStream> transportStreams)
    {
        NetworkId = section.TableIdExtension;
        VersionNumber = section.VersionNumber;
        SectionNumber = section.SectionNumber;
        LastSectionNumber = section.LastSectionNumber;
        IsActualNetwork = section.TableId == ActualNetworkTableId;
        NetworkDescriptors = networkDescriptors;
        TransportStreams = transportStreams;

        NetworkName = networkDescriptors.WithTag(DescriptorTags.NetworkName) is { } name
            && NetworkNameDescriptor.TryRead(name, out var read)
                ? read
                : string.Empty;
    }

    public int NetworkId { get; }

    public int VersionNumber { get; }

    public int SectionNumber { get; }

    public int LastSectionNumber { get; }

    public bool IsActualNetwork { get; }

    public string NetworkName { get; }

    public IReadOnlyList<Descriptor> NetworkDescriptors { get; }

    public IReadOnlyList<NetworkTransportStream> TransportStreams { get; }

    public static TableRead<NetworkInformationTable> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId is not (ActualNetworkTableId or OtherNetworkTableId))
        {
            return Rejected(TableDefect.WrongTableId);
        }

        var body = section.Body;

        if (body.Length < 4)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        var span = body.Span;
        var networkDescriptorsLength = ((span[0] & 0x0F) << 8) | span[1];

        if (2 + networkDescriptorsLength + 2 > body.Length)
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        if (!DescriptorLoop.TryRead(body.Slice(2, networkDescriptorsLength), out var networkDescriptors))
        {
            return Rejected(TableDefect.MalformedDescriptor);
        }

        var at = 2 + networkDescriptorsLength;
        var loopLength = ((span[at] & 0x0F) << 8) | span[at + 1];
        at += 2;

        if (at + loopLength != body.Length)
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        var transportStreams = new List<NetworkTransportStream>();
        var end = at + loopLength;

        while (at < end)
        {
            if (end - at < TransportStreamHeaderSize)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            var descriptorsLength = ((span[at + 4] & 0x0F) << 8) | span[at + 5];

            if (at + TransportStreamHeaderSize + descriptorsLength > end)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            if (!DescriptorLoop.TryRead(
                    body.Slice(at + TransportStreamHeaderSize, descriptorsLength),
                    out var descriptors))
            {
                return Rejected(TableDefect.MalformedDescriptor);
            }

            transportStreams.Add(new NetworkTransportStream(
                (span[at] << 8) | span[at + 1],
                (span[at + 2] << 8) | span[at + 3],
                descriptors));

            at += TransportStreamHeaderSize + descriptorsLength;
        }

        return new TableRead<NetworkInformationTable>.Parsed(
            new NetworkInformationTable(section, networkDescriptors, transportStreams));
    }

    private static TableRead<NetworkInformationTable> Rejected(TableDefect defect)
        => new TableRead<NetworkInformationTable>.Rejected(defect);
}
