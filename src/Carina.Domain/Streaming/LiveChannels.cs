namespace Carina.Domain.Streaming;

public static class LiveChannels
{
    public static IReadOnlyList<LiveChannel> Carrying { get; } =
    [
        LiveChannel.PictureHeader,
        LiveChannel.Picture,
        LiveChannel.SoundHeader,
        LiveChannel.Sound,
        LiveChannel.Control,
    ];

    public static IReadOnlyList<LiveChannel> SetAsideForLater { get; } =
    [
        LiveChannel.CaptionHeader,
        LiveChannel.Caption,
        LiveChannel.ServiceInformation,
    ];

    public static IReadOnlyList<LiveChannel> Headers { get; } =
    [
        LiveChannel.PictureHeader,
        LiveChannel.SoundHeader,
    ];

    public static IReadOnlyList<LiveChannel> Expendable { get; } =
    [
        LiveChannel.Picture,
        LiveChannel.Sound,
    ];
}
