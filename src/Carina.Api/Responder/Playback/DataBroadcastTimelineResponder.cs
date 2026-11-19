using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Api.Responder.Playback;

public sealed record DataBroadcastVersionResponder(int Id, int Version, double FromSec, double ToSec, long Size)
{
    public static DataBroadcastVersionResponder Of(PlacedVersion placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        return new DataBroadcastVersionResponder(
            placed.Module.ModuleId,
            placed.Module.Version,
            placed.From.TotalSeconds,
            placed.To.TotalSeconds,
            DataBroadcastFrames.ModuleHeaderLength + placed.Module.EntityBytes);
    }
}

public sealed record DataBroadcastCarouselResponder(int Tag, uint DownloadId, IReadOnlyList<DataBroadcastVersionResponder> Versions)
{
    public static DataBroadcastCarouselResponder Of(PlacedCarousel carousel)
    {
        ArgumentNullException.ThrowIfNull(carousel);

        return new DataBroadcastCarouselResponder(
            carousel.Tag,
            carousel.DownloadId,
            [.. carousel.Versions.Select(DataBroadcastVersionResponder.Of)]);
    }
}

public sealed record DataBroadcastEventResponder(
    int Group,
    int Id,
    int Type,
    bool Immediate,
    double AtSec,
    byte[] PrivateData,
    int? ComponentTag)
{
    public static DataBroadcastEventResponder Of(PlacedEvent placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        return new DataBroadcastEventResponder(
            placed.Message.Group,
            placed.Message.Id,
            placed.Message.MessageType,
            placed.Message.IsImmediate,
            placed.At.TotalSeconds,
            placed.Message.PrivateData.ToArray(),
            placed.Message.ComponentTag);
    }
}

public sealed record DataBroadcastTimelineResponder(
    int EntryTag,
    bool AutoStart,
    string Startup,
    bool Incomplete,
    IReadOnlyList<DataBroadcastCarouselResponder> Carousels,
    IReadOnlyList<DataBroadcastEventResponder> Events)
{
    public static DataBroadcastTimelineResponder Of(DataBroadcastTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return new DataBroadcastTimelineResponder(
            timeline.EntryTag,
            timeline.AutoStart,
            timeline.StartupDocument,
            timeline.Incomplete,
            [.. timeline.Carousels.Select(DataBroadcastCarouselResponder.Of)],
            [.. timeline.Events.Select(DataBroadcastEventResponder.Of)]);
    }
}
