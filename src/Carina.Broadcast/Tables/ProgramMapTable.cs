using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

public sealed class ProgramMapTable
{
    public const int TableId = 0x02;

    public const int NoPcrPid = 0x1FFF;

    private const int FixedFieldsSize = 4;

    private const int StreamHeaderSize = 5;

    private ProgramMapTable(
        Section section,
        int? pcrPid,
        IReadOnlyList<Descriptor> descriptors,
        IReadOnlyList<ElementaryStream> streams)
    {
        ProgramNumber = section.TableIdExtension;
        VersionNumber = section.VersionNumber;
        PcrPid = pcrPid;
        Descriptors = descriptors;
        Streams = streams;
    }

    public int ProgramNumber { get; }

    public int VersionNumber { get; }

    public int? PcrPid { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }

    public IReadOnlyList<ElementaryStream> Streams { get; }

    public static TableRead<ProgramMapTable> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId)
        {
            return Rejected(TableDefect.WrongTableId);
        }

        ReadOnlyMemory<byte> body = section.Body;

        if (body.Length < FixedFieldsSize)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        ReadOnlySpan<byte> span = body.Span;
        int programInfoLength = ((span[2] & 0x0F) << 8) | span[3];

        if (FixedFieldsSize + programInfoLength > body.Length)
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        if (!DescriptorLoop.TryRead(body.Slice(FixedFieldsSize, programInfoLength), out IReadOnlyList<Descriptor>? descriptors))
        {
            return Rejected(TableDefect.MalformedDescriptor);
        }

        var streams = new List<ElementaryStream>();
        int at = FixedFieldsSize + programInfoLength;

        while (at < body.Length)
        {
            if (body.Length - at < StreamHeaderSize)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            int infoLength = ((span[at + 3] & 0x0F) << 8) | span[at + 4];

            if (at + StreamHeaderSize + infoLength > body.Length)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            if (!DescriptorLoop.TryRead(body.Slice(at + StreamHeaderSize, infoLength), out IReadOnlyList<Descriptor>? streamDescriptors))
            {
                return Rejected(TableDefect.MalformedDescriptor);
            }

            streams.Add(new ElementaryStream(span[at], ((span[at + 1] & 0x1F) << 8) | span[at + 2], streamDescriptors));
            at += StreamHeaderSize + infoLength;
        }

        return new TableRead<ProgramMapTable>.Parsed(
            new ProgramMapTable(section, PcrPidOf(((span[0] & 0x1F) << 8) | span[1]), descriptors, streams));
    }

    private static int? PcrPidOf(int carried) => carried == NoPcrPid ? null : carried;

    private static TableRead<ProgramMapTable> Rejected(TableDefect defect)
        => new TableRead<ProgramMapTable>.Rejected(defect);
}
