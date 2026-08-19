using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;

namespace Carina.BroadcastTestSupport;

public sealed record SyntheticProgramme(int EventId, DateTimeOffset StartsAt, TimeSpan? Runs)
{
    public string? Name { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<(int ServiceId, int EventId)> SharedWith { get; init; } = [];

    public IReadOnlyList<(int ServiceId, int EventId)> RelaysTo { get; init; } = [];
}

public sealed record SyntheticGuideService(int ServiceId, string Name)
{
    public ServiceKind Kind { get; init; } = ServiceKind.Television;

    public IReadOnlyList<SyntheticProgramme> Programmes { get; init; } = [];

    public bool MissingSegment { get; init; }
}

public sealed record SyntheticGuide
{
    public required int NetworkId { get; init; }

    public required int TransportStreamId { get; init; }

    public IReadOnlyList<SyntheticGuideService> Services { get; init; } = [];

    public bool WithExtended { get; init; } = true;

    public bool WithDescription { get; init; } = true;

    public int CorruptSections { get; init; }

    public byte[] ToBytes()
    {
        var packets = new List<byte[]>();

        if (WithDescription)
        {
            packets.AddRange(new TransportStreamWriter(ServiceDescriptionTable.Pid)
                .Sections(DescriptionSection())
                .Packets);
        }

        packets.AddRange(new TransportStreamWriter(EventInformationTable.Pid)
            .Sections([.. EventSections()])
            .Packets);

        return [.. packets.SelectMany(packet => packet)];
    }

    private static byte[] Text(string text)
        => new AribTextWriter().DesignateAlphanumericToG0().Ascii(text).ToArray();

    private IEnumerable<byte[]> EventSections()
    {
        for (int corrupted = 0; corrupted < CorruptSections; corrupted++)
        {
            yield return EventSection(
                EventInformationTable.FirstScheduleActualTableId,
                Services[0],
                [],
                corrupt: true);
        }

        foreach (SyntheticGuideService service in Services)
        {
            yield return EventSection(
                EventInformationTable.FirstScheduleActualTableId,
                service,
                [.. service.Programmes.Select(programme => Event(service, programme))]);

            if (WithExtended)
            {
                yield return EventSection(
                    EventInformationTable.FirstScheduleActualTableId + 8,
                    service,
                    []);
            }
        }
    }

    private byte[] EventSection(
        int tableId,
        SyntheticGuideService service,
        byte[][] events,
        bool corrupt = false)
        => new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = service.ServiceId,
            VersionNumber = 1,
            LastSectionNumber = service.MissingSegment && !corrupt ? ScheduleProgress.SectionsPerSegment : 0,
            CorruptChecksum = corrupt,
            Body = new EitWriter
            {
                TransportStreamId = TransportStreamId,
                OriginalNetworkId = NetworkId,
                LastTableId = tableId,
                Events = events,
            }.ToBody(),
        }.ToBytes();

    private static byte[] Event(SyntheticGuideService service, SyntheticProgramme programme)
    {
        var descriptors = new List<byte[]>();

        if (programme.Name is { } name)
        {
            descriptors.Add(SiDescriptorWriter.ShortEvent(Text(name), Text(programme.Summary)));
        }

        if (programme.SharedWith.Count > 0)
        {
            descriptors.Add(SiDescriptorWriter.EventGroup(
                EventGroupKind.Shared,
                [(service.ServiceId, programme.EventId), .. programme.SharedWith]));
        }

        if (programme.RelaysTo.Count > 0)
        {
            descriptors.Add(SiDescriptorWriter.EventGroup(
                EventGroupKind.Relayed,
                [(service.ServiceId, programme.EventId), .. programme.RelaysTo]));
        }

        return EitWriter.Event(
            programme.EventId,
            programme.StartsAt,
            programme.Runs,
            DescriptorWriter.Loop([.. descriptors]));
    }

    private byte[] DescriptionSection()
        => new SectionWriter
        {
            TableId = ServiceDescriptionTable.ActualStreamTableId,
            TableIdExtension = TransportStreamId,
            Body = new SdtWriter
            {
                OriginalNetworkId = NetworkId,
                Services =
                [
                    .. Services.Select(service => SdtWriter.Service(
                        service.ServiceId,
                        SiDescriptorWriter.Service(
                            (int)service.Kind,
                            Text("Carina"),
                            Text(service.Name)))),
                ],
            }.ToBody(),
        }.ToBytes();
}
