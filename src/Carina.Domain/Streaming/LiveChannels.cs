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
        LiveChannel.Control,
    ];

    public static IReadOnlyList<LiveChannel> SetAsideForLater { get; } =
    [
        LiveChannel.ServiceInformation,
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
    ];

    public static IReadOnlyList<LiveChannel> Expendable { get; } =
    [
        LiveChannel.Picture,
        LiveChannel.Sound,
    ];
}
