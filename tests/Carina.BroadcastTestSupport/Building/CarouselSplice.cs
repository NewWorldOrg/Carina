using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.BroadcastTestSupport;

/// <summary>
/// Puts a data carousel into a transport stream ffmpeg wrote: every programme map of the programme is written
/// again listing the carousel's stream beside what ffmpeg listed, and the carousel's sections are carried, over
/// and over, one packet after every so many of the stream's own. Each time round, the carousel is asked for
/// afresh by how many times it has gone round before.
/// </summary>
public static class CarouselSplice
{
    public const int CarouselPid = CarouselBroadcast.CarouselPid;

    public static byte[] Into(byte[] stream, int programNumber, int mapPid, Func<int, IReadOnlyList<byte[]>> carousel, int every)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(carousel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(every);

        byte[] map = Remapped(stream, programNumber, mapPid);
        TransportStreamWriter maps = new(mapPid);
        TransportStreamWriter carried = new(CarouselPid);
        Queue<byte[]> waiting = [];
        List<byte> spliced = [];
        int rounds = 0;

        for (int at = 0; at + TransportStreamWriter.PacketSize <= stream.Length; at += TransportStreamWriter.PacketSize)
        {
            byte[] packet = stream[at..(at + TransportStreamWriter.PacketSize)];

            if (!TransportPacket.TryRead(packet, out TransportPacket read) || read.Pid != mapPid)
            {
                spliced.AddRange(packet);
            }
            else if (read.PayloadUnitStart)
            {
                spliced.AddRange(Written(maps, map));
            }

            if ((at / TransportStreamWriter.PacketSize) % every == 0)
            {
                spliced.AddRange(Next(carried, carousel, waiting, ref rounds));
            }
        }

        return [.. spliced];
    }

    private static byte[] Remapped(byte[] stream, int programNumber, int mapPid)
    {
        ProgramMapTable written = FirstMap(stream, mapPid);

        return new PmtWriter
        {
            ProgramNumber = programNumber,
            PcrPid = written.PcrPid ?? PmtWriter.NoPcr,
            Descriptors = Loop(written.Descriptors),
            Streams =
            [
                .. written.Streams.Select(carried => PmtWriter.Stream(carried.StreamType, carried.Pid, Loop(carried.Descriptors))),
                BxmlInfoWriter.DataBroadcastStream(
                    CarouselPid,
                    CarouselBroadcast.EntryTag,
                    BxmlInfoWriter.Entry(false, documentResolution: 1, bmlMajorVersion: 1, bmlMinorVersion: 0, CarouselBroadcast.DataEventId, defaultVersion: true)),
            ],
        }.ToBytes();
    }

    private static ProgramMapTable FirstMap(byte[] stream, int mapPid)
    {
        SectionAssembler assembler = new(mapPid);

        for (int at = 0; at + TransportStreamWriter.PacketSize <= stream.Length; at += TransportStreamWriter.PacketSize)
        {
            foreach (SectionRead read in assembler.Push(stream.AsSpan(at, TransportStreamWriter.PacketSize)))
            {
                if (read is SectionRead.Assembled assembled
                    && ProgramMapTable.Read(assembled.Section) is TableRead<ProgramMapTable>.Parsed parsed)
                {
                    return parsed.Table;
                }
            }
        }

        throw new InvalidOperationException($"The stream carries no programme map on pid {mapPid}.");
    }

    private static byte[] Loop(IReadOnlyList<Descriptor> descriptors)
        => DescriptorWriter.Loop([.. descriptors.Select(descriptor => DescriptorWriter.Of(descriptor.Tag, descriptor.Payload.ToArray()))]);

    private static IEnumerable<byte> Written(TransportStreamWriter writer, byte[] section)
    {
        int before = writer.Packets.Count;

        writer.Sections(section);

        return writer.Packets.Skip(before).SelectMany(packet => packet);
    }

    private static byte[] Next(TransportStreamWriter writer, Func<int, IReadOnlyList<byte[]>> carousel, Queue<byte[]> waiting, ref int rounds)
    {
        if (waiting.Count is 0)
        {
            int before = writer.Packets.Count;

            writer.Sections([.. carousel(rounds++)]);

            foreach (byte[] packet in writer.Packets.Skip(before))
            {
                waiting.Enqueue(packet);
            }
        }

        return waiting.Dequeue();
    }
}
