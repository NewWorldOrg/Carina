namespace Carina.Domain.Streaming;

public static class LiveChannels
{
    public static IReadOnlyList<LiveChannel> Carrying { get; } =
    [
        LiveChannel.PictureHeader,
        LiveChannel.Picture,
        LiveChannel.SoundHeader,
        LiveChannel.Sound,
        LiveChannel.CaptionHeader,
        LiveChannel.Caption,
        LiveChannel.DataBroadcast,
        LiveChannel.Control,
    ];

    public static IReadOnlyList<LiveChannel> Headers { get; } =
    [
        LiveChannel.PictureHeader,
        LiveChannel.SoundHeader,
        LiveChannel.CaptionHeader,
    ];

    public static IReadOnlyList<LiveChannel> Kept { get; } =
    [
        .. Headers,
        LiveChannel.Caption,
        LiveChannel.DataBroadcast,
    ];

    public static IReadOnlyList<LiveChannel> Expendable { get; } =
    [
        LiveChannel.Picture,
        LiveChannel.Sound,
    ];

    /// <summary>
    /// The channels a viewer whose backlog is full goes without, as it goes without pictures: the expendable
    /// ones, which the backlog counts, and the data broadcast, which it does not.
    /// </summary>
    public static IReadOnlyList<LiveChannel> CutWhenBehind { get; } =
    [
        .. Expendable,
        LiveChannel.DataBroadcast,
    ];
}
