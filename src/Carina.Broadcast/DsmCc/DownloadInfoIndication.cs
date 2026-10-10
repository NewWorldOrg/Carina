using System.Buffers.Binary;
using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public sealed class DownloadInfoIndication
{
    public const int TableId = 0x3B;

    public const int MessageId = 0x1002;

    private const int FixedFieldsSize = 16;

    private const int LengthFieldSize = 2;

    private const int ModuleHeaderSize = 8;

    private DownloadInfoIndication(uint transactionId, uint downloadId, int blockSize, IReadOnlyList<ModuleInfo> modules)
    {
        TransactionId = transactionId;
        DownloadId = downloadId;
        BlockSize = blockSize;
        Modules = modules;
    }

    public uint TransactionId { get; }

    public uint DownloadId { get; }

    public int BlockSize { get; }

    public IReadOnlyList<ModuleInfo> Modules { get; }

    public static TableRead<DownloadInfoIndication> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId)
        {
            return Rejected(TableDefect.WrongTableId);
        }

        if (!DsmCcMessage.TryRead(section.Body, MessageId, out uint transactionId, out ReadOnlyMemory<byte> message, out TableDefect defect))
        {
            return Rejected(defect);
        }

        if (message.Length < FixedFieldsSize + LengthFieldSize)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        ReadOnlySpan<byte> span = message.Span;
        int at = FixedFieldsSize;

        if (!TrySkipLengthPrefixed(span, ref at) || span.Length - at < LengthFieldSize)
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(span[at..]);
        at += LengthFieldSize;
        var modules = new List<ModuleInfo>(Math.Min(count, span.Length / ModuleHeaderSize));

        for (int index = 0; index < count; index++)
        {
            TableDefect? broken = TryReadModule(message, ref at, modules);

            if (broken is { } moduleDefect)
            {
                return Rejected(moduleDefect);
            }
        }

        if (!TrySkipLengthPrefixed(span, ref at))
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        return new TableRead<DownloadInfoIndication>.Parsed(new DownloadInfoIndication(
            transactionId,
            BinaryPrimitives.ReadUInt32BigEndian(span),
            BinaryPrimitives.ReadUInt16BigEndian(span[4..]),
            modules));
    }

    private static TableDefect? TryReadModule(ReadOnlyMemory<byte> message, ref int at, List<ModuleInfo> modules)
    {
        ReadOnlySpan<byte> span = message.Span;

        if (span.Length - at < ModuleHeaderSize)
        {
            return TableDefect.LoopOverrun;
        }

        int infoLength = span[at + 7];

        if (at + ModuleHeaderSize + infoLength > span.Length)
        {
            return TableDefect.LoopOverrun;
        }

        if (!DescriptorLoop.TryRead(message.Slice(at + ModuleHeaderSize, infoLength), out IReadOnlyList<Descriptor>? descriptors)
            || !ModuleInfo.TryRead(
                BinaryPrimitives.ReadUInt16BigEndian(span[at..]),
                BinaryPrimitives.ReadUInt32BigEndian(span[(at + 2)..]),
                span[at + 6],
                descriptors,
                out ModuleInfo? module))
        {
            return TableDefect.MalformedDescriptor;
        }

        modules.Add(module);
        at += ModuleHeaderSize + infoLength;

        return null;
    }

    private static bool TrySkipLengthPrefixed(ReadOnlySpan<byte> span, ref int at)
    {
        if (span.Length - at < LengthFieldSize)
        {
            return false;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(span[at..]);

        if (at + LengthFieldSize + length > span.Length)
        {
            return false;
        }

        at += LengthFieldSize + length;

        return true;
    }

    private static TableRead<DownloadInfoIndication> Rejected(TableDefect defect)
        => new TableRead<DownloadInfoIndication>.Rejected(defect);
}
