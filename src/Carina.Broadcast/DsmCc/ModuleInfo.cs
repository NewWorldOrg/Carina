using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Text;

namespace Carina.Broadcast.DsmCc;

public sealed class ModuleInfo
{
    private const int LanguageCodeSize = 3;

    private const int CompressionTypeSize = 5;

    private ModuleInfo(
        int moduleId,
        long moduleSize,
        int moduleVersion,
        IReadOnlyList<Descriptor> descriptors,
        ModuleInformation? info,
        ModuleCompression? compression)
    {
        ModuleId = moduleId;
        ModuleSize = moduleSize;
        ModuleVersion = moduleVersion;
        Descriptors = descriptors;
        Type = Latin(descriptors.WithTag(ModuleDescriptorTags.Type));
        Name = Latin(descriptors.WithTag(ModuleDescriptorTags.Name));
        Info = info;
        Compression = compression;
    }

    public int ModuleId { get; }

    public long ModuleSize { get; }

    public int ModuleVersion { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }

    public string? Type { get; }

    public string? Name { get; }

    public ModuleInformation? Info { get; }

    public ModuleCompression? Compression { get; }

    internal static bool TryRead(
        int moduleId,
        long moduleSize,
        int moduleVersion,
        IReadOnlyList<Descriptor> descriptors,
        [NotNullWhen(true)] out ModuleInfo? module)
    {
        module = null;
        Descriptor? info = descriptors.WithTag(ModuleDescriptorTags.Info);
        Descriptor? compression = descriptors.WithTag(ModuleDescriptorTags.CompressionType);

        if (info is { Payload.Length: < LanguageCodeSize } || compression is { Payload.Length: < CompressionTypeSize })
        {
            return false;
        }

        module = new ModuleInfo(
            moduleId,
            moduleSize,
            moduleVersion,
            descriptors,
            info is null ? null : InformationOf(info.Payload.Span),
            compression is null ? null : CompressionOf(compression.Payload.Span));

        return true;
    }

    private static ModuleInformation InformationOf(ReadOnlySpan<byte> payload)
        => new(Encoding.Latin1.GetString(payload[..LanguageCodeSize]), AribText.Decode(payload[LanguageCodeSize..]));

    private static ModuleCompression CompressionOf(ReadOnlySpan<byte> payload)
        => new(payload[0], BinaryPrimitives.ReadUInt32BigEndian(payload[1..]));

    private static string? Latin(Descriptor? descriptor)
        => descriptor is null ? null : Encoding.Latin1.GetString(descriptor.Payload.Span);
}
