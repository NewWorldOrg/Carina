namespace Carina.Domain.Captions;

/// <summary>
/// One change of the caption text on screen, at a moment on the file's own 90 kHz clock as ffmpeg reads it,
/// on the same clock as <see cref="CaptionCue"/>: the text shown, line by line, or the screen cleared when
/// there is none.
/// </summary>
public sealed record CaptionLine
{
    public CaptionLine(long pts, string? text)
    {
        if (text is { Length: 0 })
        {
            throw new ArgumentException("A screen with no text on it is cleared, which is said with no text at all.", nameof(text));
        }

        Pts = pts;
        Text = text;
    }

    public long Pts { get; }

    public string? Text { get; }

    public bool Clears => Text is null;

    public TimeSpan At => TimeSpan.FromTicks((long)((Int128)Pts * TimeSpan.TicksPerSecond / CaptionCue.Hertz));
}
