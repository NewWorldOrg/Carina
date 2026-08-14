using Carina.Broadcast.Sections;

namespace Carina.BroadcastTestSupport;

public static class CarriedSection
{
    public const int SomePid = 0x0010;

    public static Section Of(SectionWriter writer)
    {
        var assembler = new SectionAssembler(SomePid);

        return new TransportStreamWriter(SomePid)
            .Sections(writer.ToBytes())
            .Packets
            .SelectMany(packet => assembler.Push(packet))
            .OfType<SectionRead.Assembled>()
            .Single()
            .Section;
    }
}
