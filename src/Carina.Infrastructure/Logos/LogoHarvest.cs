using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Images;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.Domain.Channels;

namespace Carina.Infrastructure.Logos;

public sealed record HarvestedLogo(int NetworkId, int LogoId, int LogoType, int LogoVersion, AribLogoImage Image);

public sealed record HarvestedLogoLink(int NetworkId, int ServiceId, int? LogoId);

public sealed class LogoHarvest
{
    private readonly SectionReader reader = new(CommonDataTable.Pid, ServiceDescriptionTable.Pid);

    private readonly Dictionary<(int Network, int Logo), HarvestedLogo> logos = [];

    private readonly Dictionary<(int Network, int Service), HarvestedLogoLink> links = [];

    private readonly byte[] carry = new byte[TransportPacket.Size];

    private int carried;

    public IReadOnlyList<HarvestedLogo> Logos => [.. logos.Values];

    public IReadOnlyList<HarvestedLogoLink> Links => [.. links.Values];

    public long UnreadablePackets => reader.UnreadablePackets;

    public void Push(ReadOnlySpan<byte> packets)
    {
        if (carried > 0)
        {
            int take = Math.Min(TransportPacket.Size - carried, packets.Length);

            packets[..take].CopyTo(carry.AsSpan(carried));
            carried += take;
            packets = packets[take..];

            if (carried < TransportPacket.Size)
            {
                return;
            }

            Read(carry);
            carried = 0;
        }

        int whole = packets.Length - (packets.Length % TransportPacket.Size);

        Read(packets[..whole]);
        packets[whole..].CopyTo(carry);
        carried = packets.Length % TransportPacket.Size;
    }

    public bool EverythingOnTheTransportIsAccountedFor(IReadOnlyList<ServiceId> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            return false;
        }

        foreach (ServiceId service in services)
        {
            HarvestedLogoLink? link = links.Values.FirstOrDefault(known => known.ServiceId == service.Value);

            if (link is null)
            {
                return false;
            }

            if (link.LogoId is { } named && !logos.ContainsKey((link.NetworkId, named)))
            {
                return false;
            }
        }

        return true;
    }

    private void Read(ReadOnlySpan<byte> packets)
    {
        foreach (SectionRead read in reader.Push(packets))
        {
            if (read is not SectionRead.Assembled assembled)
            {
                continue;
            }

            if (assembled.Pid == CommonDataTable.Pid)
            {
                TakeTheLogo(assembled.Section);

                continue;
            }

            TakeTheLinks(assembled.Section);
        }
    }

    private void TakeTheLogo(Section section)
    {
        if (CommonDataTable.Read(section) is not TableRead<CommonDataTable>.Parsed parsed
            || parsed.Table.Logo is not { Image: { } image } carriedLogo)
        {
            return;
        }

        var found = new HarvestedLogo(
            parsed.Table.OriginalNetworkId,
            carriedLogo.LogoId,
            carriedLogo.LogoType,
            carriedLogo.LogoVersion,
            image);

        if (!logos.TryGetValue((found.NetworkId, found.LogoId), out HarvestedLogo? held)
            || IsWorthKeepingOver(found, held))
        {
            logos[(found.NetworkId, found.LogoId)] = found;
        }
    }

    private static bool IsWorthKeepingOver(HarvestedLogo arriving, HarvestedLogo held)
    {
        int arrived = arriving.Image.Width * arriving.Image.Height;
        int kept = held.Image.Width * held.Image.Height;

        return arrived > kept || (arrived == kept && arriving.LogoVersion != held.LogoVersion);
    }

    private void TakeTheLinks(Section section)
    {
        if (ServiceDescriptionTable.Read(section) is not TableRead<ServiceDescriptionTable>.Parsed parsed
            || !parsed.Table.IsActualStream)
        {
            return;
        }

        foreach (DescribedService service in parsed.Table.Services)
        {
            if (service.Descriptors.WithTag(DescriptorTags.LogoTransmission) is not { } descriptor
                || !LogoTransmission.TryRead(descriptor, out LogoTransmission? transmission))
            {
                continue;
            }

            links[(parsed.Table.OriginalNetworkId, service.ServiceId)] = new HarvestedLogoLink(
                parsed.Table.OriginalNetworkId,
                service.ServiceId,
                transmission is LogoTransmission.InTheCommonDataTable named ? named.LogoId : null);
        }
    }
}
