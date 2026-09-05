using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

public sealed class CommonDataTable
{
    public const int Pid = 0x0029;

    public const int TableId = 0xC8;

    public const int LogoDataType = 0x01;

    private const int FixedFieldsSize = 5;

    private const int LogoModuleHeaderSize = 7;

    private CommonDataTable(
        Section section,
        int originalNetworkId,
        int dataType,
        IReadOnlyList<Descriptor> descriptors,
        ReadOnlyMemory<byte> dataModule,
        CarriedLogo? logo)
    {
        DownloadDataId = section.TableIdExtension;
        OriginalNetworkId = originalNetworkId;
        VersionNumber = section.VersionNumber;
        SectionNumber = section.SectionNumber;
        LastSectionNumber = section.LastSectionNumber;
        DataType = dataType;
        Descriptors = descriptors;
        DataModule = dataModule;
        Logo = logo;
    }

    public int DownloadDataId { get; }

    public int OriginalNetworkId { get; }

    public int VersionNumber { get; }

    public int SectionNumber { get; }

    public int LastSectionNumber { get; }

    public int DataType { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }

    public ReadOnlyMemory<byte> DataModule { get; }

    public CarriedLogo? Logo { get; }

    public bool CarriesALogo => DataType == LogoDataType;

    public static TableRead<CommonDataTable> Read(Section section)
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
        int descriptorsLength = ((span[3] & 0x0F) << 8) | span[4];

        if (FixedFieldsSize + descriptorsLength > body.Length)
        {
            return Rejected(TableDefect.LoopOverrun);
        }

        if (!DescriptorLoop.TryRead(
                body.Slice(FixedFieldsSize, descriptorsLength),
                out IReadOnlyList<Descriptor>? descriptors))
        {
            return Rejected(TableDefect.MalformedDescriptor);
        }

        int dataType = span[2];
        ReadOnlyMemory<byte> dataModule = body[(FixedFieldsSize + descriptorsLength)..];
        CarriedLogo? logo = null;

        if (dataType == LogoDataType && !TryReadLogo(dataModule, out logo))
        {
            return Rejected(TableDefect.DataModuleOverrun);
        }

        return new TableRead<CommonDataTable>.Parsed(
            new CommonDataTable(
                section,
                (span[0] << 8) | span[1],
                dataType,
                descriptors,
                dataModule,
                logo));
    }

    private static bool TryReadLogo(ReadOnlyMemory<byte> dataModule, out CarriedLogo? logo)
    {
        logo = null;

        if (dataModule.Length < LogoModuleHeaderSize)
        {
            return false;
        }

        ReadOnlySpan<byte> module = dataModule.Span;
        int size = (module[5] << 8) | module[6];

        if (LogoModuleHeaderSize + size > dataModule.Length)
        {
            return false;
        }

        logo = new CarriedLogo(
            module[0],
            ((module[1] & 0x01) << 8) | module[2],
            ((module[3] & 0x0F) << 8) | module[4],
            dataModule.Slice(LogoModuleHeaderSize, size));

        return true;
    }

    private static TableRead<CommonDataTable> Rejected(TableDefect defect)
        => new TableRead<CommonDataTable>.Rejected(defect);
}
