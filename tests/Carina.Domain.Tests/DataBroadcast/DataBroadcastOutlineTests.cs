using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastOutlineTests
{
    [Fact(DisplayName = "BR-BA-001: the outline of a record tells every version, its sightings and the bytes of its resources, without the resources")]
    public void TheOutlineOfARecordTellsEveryVersionWithoutItsResources()
    {
        DataBroadcastRecord record = new(
            1_000,
            Carousels.Entry,
            [
                new RecordedCarousel(Carousels.Other, 2, [Carousels.Version(Carousels.Other, 0, 1, 50, bodyLength: 30)]),
                new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, 2, 200), Carousels.Version(Carousels.Entry, 0, 1, 100)]),
            ],
            [Carousels.Event(1, 150)],
            true,
            autoStart: true);

        DataBroadcastOutline outline = record.Outline;

        Assert.Equal(
            (record.StartsAt, record.Start, record.EntryTag, record.AutoStart, record.Incomplete, record.StartupDocument),
            (outline.StartsAt, outline.Start, outline.EntryTag, outline.AutoStart, outline.Incomplete, outline.StartupDocument));
        Assert.Equal(
            ["40/1 0/1 100-100 " + Bytes(record, 0, 0), "40/1 0/2 200-200 " + Bytes(record, 0, 1), "50/2 0/1 50-50 " + Bytes(record, 1, 0)],
            outline.Carousels.SelectMany(carousel => carousel.Versions.Select(version =>
                $"{carousel.Tag:x2}/{carousel.DownloadId} {version.ModuleId}/{version.Version} {version.FirstSeen}-{version.LastSeen} {version.EntityBytes}")));
        Assert.Equal(record.Events, outline.Events);
    }

    [Fact(DisplayName = "BR-BD-005: an outline refuses what a record refuses: a download of a carousel twice, a version twice, a version last seen before it was first seen")]
    public void AnOutlineRefusesWhatARecordRefuses()
    {
        OutlinedVersion version = new(0, 1, 10, 20, 15);

        Assert.Throws<ArgumentException>(() => new DataBroadcastOutline(0, Carousels.Entry, [new OutlinedCarousel(Carousels.Entry, 1, []), new OutlinedCarousel(Carousels.Entry, 1, [])], [], false, false));
        Assert.Throws<ArgumentException>(() => new OutlinedCarousel(Carousels.Entry, 1, [version, version]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutlinedVersion(0, 1, 20, 10, 15));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutlinedVersion(0, 1, 10, 20, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutlinedVersion(0x10000, 1, 10, 20, 15));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutlinedCarousel(0x100, 1, []));
    }

    private static string Bytes(DataBroadcastRecord record, int carousel, int version)
    {
        ModuleVersion held = record.Carousels[carousel].Versions[version];

        return (held.Bytes - ModuleVersion.HeaderBytes).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
