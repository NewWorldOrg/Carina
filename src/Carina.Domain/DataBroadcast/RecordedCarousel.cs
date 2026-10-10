namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One carousel of a recording's data broadcast: its component tag, its download id, and every module version
/// it carried in the order they were first seen. Versions first seen at the same moment keep the order they
/// arrived in, which is the order they are given in.
/// </summary>
public sealed record RecordedCarousel
{
    public const int HeaderBytes = sizeof(byte) + sizeof(uint) + sizeof(ushort);

    public RecordedCarousel(int tag, uint downloadId, IReadOnlyList<ModuleVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        int checkedTag = CarouselNumbers.Tag(tag, nameof(tag));

        if (versions.Any(version => version.Tag != checkedTag))
        {
            throw new ArgumentException("A carousel holds only versions of its own modules.", nameof(versions));
        }

        if (versions.Select(version => (version.ModuleId, version.Version)).Distinct().Count() != versions.Count)
        {
            throw new ArgumentException("A carousel holds each version of a module once.", nameof(versions));
        }

        Tag = checkedTag;
        DownloadId = downloadId;
        Versions = [.. versions.OrderBy(version => version.FirstSeen)];
    }

    public int Tag { get; }

    public uint DownloadId { get; }

    public IReadOnlyList<ModuleVersion> Versions { get; }

    public long Bytes => HeaderBytes + Versions.Sum(version => version.Bytes);
}
