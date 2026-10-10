using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Api.Responder.Playback;

/// <summary>
/// One version of a module running over the seconds of the source played, counted from its own zero, and the bytes
/// the module surface answers for it.
/// </summary>
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
            DataBroadcastFrames.ModulePayloadLength(placed.Module));
    }
}

/// <summary>
/// One download of a carousel with its versions in the order they were first seen.
/// </summary>
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

/// <summary>
/// One event message at the second of the source played it fires at, with its group, id, type, whether it was sent
/// to fire at once, and its private data.
/// </summary>
public sealed record DataBroadcastEventResponder(int Group, int Id, int Type, bool Immediate, double AtSec, byte[] PrivateData)
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
            placed.Message.PrivateData.ToArray());
    }
}

/// <summary>
/// The catalog of a recording's data broadcast over the source played: where it is entered, whether the broadcaster
/// asks for it to open by itself, the document it opens on, whether versions were left out, every version of every
/// carousel and every event message.
/// </summary>
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
