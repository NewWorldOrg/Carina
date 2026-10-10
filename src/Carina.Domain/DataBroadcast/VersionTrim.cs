namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One module version held for a record, with the carousel and the download it came in.
/// </summary>
internal readonly record struct HeldVersion(int Tag, uint DownloadId, ModuleVersion Version);

/// <summary>
/// Which versions a record leaves out to take no more than the bytes it may and to hold no more versions in a
/// carousel than a count of two bytes tells: versions that a later version of the same module of the same download
/// took the place of, oldest first, never the latest version of a module or a version of the startup document.
/// </summary>
internal static class VersionTrim
{
    public const int MostVersionsInACarousel = ushort.MaxValue;

    public static IReadOnlyList<HeldVersion> LeftOut(IReadOnlyList<HeldVersion> held, int entryTag, long bytes, long mostBytes)
    {
        Dictionary<(int Tag, uint DownloadId), int> counts = held
            .CountBy(version => (version.Tag, version.DownloadId))
            .ToDictionary(carousel => carousel.Key, carousel => carousel.Value);
        long over = bytes - mostBytes;
        List<HeldVersion> left = [];

        foreach (HeldVersion candidate in Superseded(held, entryTag))
        {
            (int Tag, uint DownloadId) carousel = (candidate.Tag, candidate.DownloadId);

            if (over <= 0 && counts[carousel] <= MostVersionsInACarousel)
            {
                continue;
            }

            left.Add(candidate);
            over -= candidate.Version.Bytes;
            counts[carousel]--;
        }

        return left;
    }

    private static IEnumerable<HeldVersion> Superseded(IReadOnlyList<HeldVersion> held, int entryTag)
    {
        HashSet<ModuleVersion> latest = new(
            held.GroupBy(version => (version.Tag, version.DownloadId, version.Version.ModuleId))
                .Select(module => module.Aggregate((kept, next) => next.Version.FirstSeen >= kept.Version.FirstSeen ? next : kept).Version),
            ReferenceEqualityComparer.Instance);

        return held
            .Where(version => !latest.Contains(version.Version) && !IsStartupDocument(version, entryTag))
            .OrderBy(version => version.Version.FirstSeen);
    }

    private static bool IsStartupDocument(HeldVersion version, int entryTag)
        => version.Tag == entryTag && version.Version.ModuleId == CarouselCatalog.StartupModuleId;
}
