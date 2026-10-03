namespace Carina.Domain.Captions;

/// <summary>
/// One change of the captions on screen, at a moment on the file's own 90 kHz clock as ffmpeg reads it,
/// which is negative before the clock came around in a file that begins shortly before it does: a picture
/// shown, or the screen cleared when there is none.
/// </summary>
public sealed record CaptionCue
{
    public const int Hertz = 90_000;

    public CaptionCue(long pts, CaptionPlacement? picture)
    {
        Pts = pts;
        Picture = picture;
    }

    public long Pts { get; }

    public CaptionPlacement? Picture { get; }

    public bool Clears => Picture is null;

    public TimeSpan At => TimeSpan.FromTicks((long)((Int128)Pts * TimeSpan.TicksPerSecond / Hertz));
}
