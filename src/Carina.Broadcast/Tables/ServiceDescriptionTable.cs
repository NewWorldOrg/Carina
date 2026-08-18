using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

public sealed class ServiceDescriptionTable
{
    public const int Pid = 0x0011;

    public const int ActualStreamTableId = 0x42;

    public const int OtherStreamTableId = 0x46;

    private const int ServiceHeaderSize = 5;

    private ServiceDescriptionTable(Section section, int originalNetworkId, IReadOnlyList<DescribedService> services)
    {
        TransportStreamId = section.TableIdExtension;
        OriginalNetworkId = originalNetworkId;
        VersionNumber = section.VersionNumber;
        SectionNumber = section.SectionNumber;
        LastSectionNumber = section.LastSectionNumber;
        IsActualStream = section.TableId == ActualStreamTableId;
        Services = services;
    }

    public int TransportStreamId { get; }

    public int OriginalNetworkId { get; }

    public int VersionNumber { get; }

    public int SectionNumber { get; }

    public int LastSectionNumber { get; }

    public bool IsActualStream { get; }

    public IReadOnlyList<DescribedService> Services { get; }

    public static TableRead<ServiceDescriptionTable> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId is not (ActualStreamTableId or OtherStreamTableId))
        {
            return Rejected(TableDefect.WrongTableId);
        }

        ReadOnlyMemory<byte> body = section.Body;

        if (body.Length < 3)
        {
            return Rejected(TableDefect.SectionTooShort);
        }

        ReadOnlySpan<byte> span = body.Span;
        var services = new List<DescribedService>();
        int at = 3;

        while (at < body.Length)
        {
            if (body.Length - at < ServiceHeaderSize)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            int descriptorsLength = ((span[at + 3] & 0x0F) << 8) | span[at + 4];

            if (at + ServiceHeaderSize + descriptorsLength > body.Length)
            {
                return Rejected(TableDefect.LoopOverrun);
            }

            if (!DescriptorLoop.TryRead(body.Slice(at + ServiceHeaderSize, descriptorsLength), out IReadOnlyList<Descriptor>? descriptors))
            {
                return Rejected(TableDefect.MalformedDescriptor);
            }

            services.Add(new DescribedService(
                (span[at] << 8) | span[at + 1],
                (span[at + 2] & 0x02) != 0,
                (span[at + 2] & 0x01) != 0,
                span[at + 3] >> 5,
                (span[at + 3] & 0x10) != 0,
                descriptors));

            at += ServiceHeaderSize + descriptorsLength;
        }

        return new TableRead<ServiceDescriptionTable>.Parsed(
            new ServiceDescriptionTable(section, (span[0] << 8) | span[1], services));
    }

    private static TableRead<ServiceDescriptionTable> Rejected(TableDefect defect)
        => new TableRead<ServiceDescriptionTable>.Rejected(defect);
}
