using System.Buffers.Binary;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public sealed class DownloadDataBlock
{
    public const int TableId = 0x3C;

    public const int MessageId = 0x1003;

    private const int FixedFieldsSize = 6;

    private DownloadDataBlock(uint downloadId, int moduleId, int moduleVersion, int blockNumber, ReadOnlyMemory<byte> data)
    {
        DownloadId = downloadId;
        ModuleId = moduleId;
        ModuleVersion = moduleVersion;
        BlockNumber = blockNumber;
        Data = data;
    }

    public uint DownloadId { get; }

    public int ModuleId { get; }

    public int ModuleVersion { get; }

    public int BlockNumber { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public static TableRead<DownloadDataBlock> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId)
        {
            return Rejected(TableDefect.WrongTableId);
        }

        if (!DsmCcMessage.TryRead(section.Body, MessageId, out uint downloadId, out ReadOnlyMemory<byte> message, out TableDefect defect))
        {
            return Rejected(defect);
        }

        if (message.Length < FixedFieldsSize)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        ReadOnlySpan<byte> span = message.Span;

        return new TableRead<DownloadDataBlock>.Parsed(new DownloadDataBlock(
            downloadId,
            BinaryPrimitives.ReadUInt16BigEndian(span),
            span[2],
            BinaryPrimitives.ReadUInt16BigEndian(span[4..]),
            message[FixedFieldsSize..]));
    }

    private static TableRead<DownloadDataBlock> Rejected(TableDefect defect)
        => new TableRead<DownloadDataBlock>.Rejected(defect);
}
