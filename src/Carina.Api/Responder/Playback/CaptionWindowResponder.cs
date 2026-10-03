using Carina.Domain.Captions;

namespace Carina.Api.Responder.Playback;

public sealed record CaptionCanvasResponder(int Width, int Height)
{
    public static CaptionCanvasResponder Of(CaptionWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new CaptionCanvasResponder(window.Width, window.Height);
    }
}

/// <summary>
/// A caption drawn on the canvas: where its top left corner sits, how large it is, and the palette PNG it
/// was drawn as.
/// </summary>
public sealed record CaptionPictureResponder(int Left, int Top, int Width, int Height, byte[] Png)
{
    public static CaptionPictureResponder Of(CaptionPlacement picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return new CaptionPictureResponder(picture.Left, picture.Top, picture.Width, picture.Height, picture.Png.ToArray());
    }
}

/// <summary>
/// One change of the captions at a second of the source played, counted from its own zero: a picture
/// shown, or the screen cleared when there is none.
/// </summary>
public sealed record CaptionCueResponder(double AtSec, CaptionPictureResponder? Picture)
{
    public static CaptionCueResponder Of(PlacedCaption caption)
    {
        ArgumentNullException.ThrowIfNull(caption);

        return new CaptionCueResponder(
            caption.At.TotalSeconds,
            caption.Picture is { } picture ? CaptionPictureResponder.Of(picture) : null);
    }
}

public sealed record CaptionWindowResponder(
    CaptionCanvasResponder Canvas,
    double UntilSec,
    IReadOnlyList<CaptionCueResponder> Cues)
{
    public static CaptionWindowResponder Of(CaptionWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new CaptionWindowResponder(
            CaptionCanvasResponder.Of(window),
            window.Until.TotalSeconds,
            [.. window.Captions.Select(CaptionCueResponder.Of)]);
    }
}
