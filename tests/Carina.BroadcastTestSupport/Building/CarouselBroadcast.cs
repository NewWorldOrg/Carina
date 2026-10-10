namespace Carina.BroadcastTestSupport;

/// <summary>
/// Writes a transport stream of one programme carrying a data broadcast, packet by packet in the order it is
/// asked for: the association table, the programme map, the programme's clock, and a carousel's download
/// info, blocks and event messages.
/// </summary>
public sealed class CarouselBroadcast
{
    public const int ProgramNumber = 1024;

    public const int TransportStreamId = 0x7FE0;

    public const int MapPid = 0x1FC8;

    public const int ClockPid = 0x0100;

    public const int CarouselPid = 0x0140;

    public const int OtherCarouselPid = 0x0141;

    public const int PictureStreamType = 0x02;

    public const int EntryTag = BxmlInfoWriter.EntryComponentTag;

    public const int OtherTag = 0x50;

    public const int DataEventId = 1;

    public const int AdaptationForTheClock = TransportStreamWriter.ProgrammeClockFieldLength;

    public const long Second = TransportStreamWriter.ProgrammeClockTicksPerSecond;

    private readonly List<byte[]> packets = [];

    private readonly Dictionary<int, TransportStreamWriter> writers = [];

    public byte[] Bytes => [.. packets.SelectMany(packet => packet)];

    public static byte[] Resource(string location, string contentType, byte[] body)
        => EntityWriter.Multipart("carina", new EntityPart(location, contentType, body));

    public CarouselBroadcast Associated(int programNumber = ProgramNumber, int mapPid = MapPid)
        => Sections(PatWriter.Pid, PatWriter.Section(TransportStreamId, (programNumber, mapPid)));

    public CarouselBroadcast Mapped(
        int version = 0,
        bool carrying = true,
        bool autoStart = false,
        int clockPid = ClockPid,
        int programNumber = ProgramNumber,
        bool withAnotherCarousel = false)
    {
        List<byte[]> streams = [PmtWriter.Stream(PictureStreamType, ClockPid, [])];

        if (carrying)
        {
            streams.Add(BxmlInfoWriter.DataBroadcastStream(
                CarouselPid,
                EntryTag,
                BxmlInfoWriter.Entry(autoStart, documentResolution: 1, bmlMajorVersion: 1, bmlMinorVersion: 0, DataEventId, defaultVersion: true)));
        }

        if (carrying && withAnotherCarousel)
        {
            streams.Add(BxmlInfoWriter.DataBroadcastStream(OtherCarouselPid, OtherTag, BxmlInfoWriter.NotEntry(DataEventId)));
        }

        SectionWriter map = new PmtWriter { ProgramNumber = programNumber, PcrPid = clockPid, Streams = [.. streams] }.ToSection();

        return Sections(
            MapPid,
            new SectionWriter { TableId = map.TableId, TableIdExtension = map.TableIdExtension, VersionNumber = version, Body = map.Body }.ToBytes());
    }

    public CarouselBroadcast At(long reference, int clockPid = ClockPid)
    {
        TransportStreamWriter writer = Writer(clockPid);
        int before = writer.Packets.Count;

        writer.Packet(null, [], AdaptationForTheClock, programmeClock: reference);
        packets.AddRange(writer.Packets.Skip(before));

        return this;
    }

    public CarouselBroadcast Listed(long transactionId, params CarouselModule[] modules)
        => Listed(CarouselPid, transactionId, modules);

    public CarouselBroadcast Listed(int pid, long transactionId, params CarouselModule[] modules)
        => Sections(pid, new DiiWriter
        {
            TransactionId = transactionId,
            BlockSize = DsmCcWriter.LargestBlock,
            Modules = [.. modules.Select(module => DiiModule.Of(module.Id, module.Body.Length, module.Version))],
        }.ToSection().ToBytes());

    public CarouselBroadcast Delivered(CarouselModule module)
        => Delivered(CarouselPid, module);

    public CarouselBroadcast Delivered(int pid, CarouselModule module)
        => Sections(
            pid,
            [.. DsmCcWriter.Blocks(1, module.Id, module.Version, module.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes())]);

    public CarouselBroadcast Fired(int version, int group, int id, int messageType, params byte[] privateData)
        => Sections(CarouselPid, new StreamDescriptorWriter
        {
            DataEventId = DataEventId,
            EventMessageGroupId = group,
            VersionNumber = version,
            Descriptors = StreamDescriptorWriter.GeneralEvent(group, StreamDescriptorWriter.Immediate, 0, messageType, id, privateData),
        }.ToSection().ToBytes());

    public CarouselBroadcast Sections(int pid, params byte[][] sections)
    {
        TransportStreamWriter writer = Writer(pid);
        int before = writer.Packets.Count;

        writer.Sections(sections);
        packets.AddRange(writer.Packets.Skip(before));

        return this;
    }

    private TransportStreamWriter Writer(int pid)
    {
        if (!writers.TryGetValue(pid, out TransportStreamWriter? writer))
        {
            writer = new TransportStreamWriter(pid);
            writers[pid] = writer;
        }

        return writer;
    }
}

/// <summary>
/// One module of a synthetic carousel: its id, its version and the entity it carries.
/// </summary>
public sealed record CarouselModule(int Id, int Version, byte[] Body);
