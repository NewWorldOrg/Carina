using System.Globalization;

using Carina.Domain.Channels;

namespace Carina.Domain.DataBroadcast;

public sealed record CarouselCatalog
{
    public const int StartupModuleId = 0x0000;

    public const string StartupResource = "startup.bml";

    public CarouselCatalog(ServiceId service, int entryTag, bool autoStart, IReadOnlyList<CatalogCarousel> carousels)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(carousels);

        if (carousels.Select(carousel => carousel.Tag).Distinct().Count() != carousels.Count)
        {
            throw new ArgumentException("A catalog holds each carousel once.", nameof(carousels));
        }

        Service = service;
        EntryTag = CarouselNumbers.Tag(entryTag, nameof(entryTag));
        AutoStart = autoStart;
        Carousels = [.. carousels];
    }

    public ServiceId Service { get; }

    public int EntryTag { get; }

    public bool AutoStart { get; }

    public string StartupDocument => StartupDocumentOf(EntryTag);

    public IReadOnlyList<CatalogCarousel> Carousels { get; }

    public static string StartupDocumentOf(int entryTag)
        => string.Create(CultureInfo.InvariantCulture, $"/{entryTag:x2}/{StartupModuleId:x4}/{StartupResource}");

    public bool CanOpen
        => Carousels.Any(carousel => carousel.Tag == EntryTag
                                     && carousel.Modules.Any(module => module.Id == StartupModuleId && module.Arrived));
}
