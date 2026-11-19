namespace Carina.Domain.DataBroadcast;

internal readonly record struct HeldVersion(int Tag, uint DownloadId, ModuleVersion Version);

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
        => held
            .GroupBy(version => (version.Tag, version.DownloadId, version.Version.ModuleId))
            .SelectMany(module => TakenThePlaceOf([.. module.OrderBy(version => version.Version.FirstSeen)]))
            .Where(superseded => !IsStartupDocument(superseded.Held, entryTag))
            .OrderBy(superseded => superseded.At)
            .ThenBy(superseded => superseded.Held.Version.FirstSeen)
            .ThenBy(superseded => superseded.Held.Tag)
            .ThenBy(superseded => superseded.Held.DownloadId)
            .ThenBy(superseded => superseded.Held.Version.ModuleId)
            .ThenBy(superseded => superseded.Held.Version.Version)
            .Select(superseded => superseded.Held);

    private static IEnumerable<Supersession> TakenThePlaceOf(List<HeldVersion> module)
        => module.Take(module.Count - 1).Select((version, at) => new Supersession(version, module[at + 1].Version.FirstSeen));

    private static bool IsStartupDocument(HeldVersion version, int entryTag)
        => version.Tag == entryTag && version.Version.ModuleId == CarouselCatalog.StartupModuleId;

    private readonly record struct Supersession(HeldVersion Held, long At);
}
