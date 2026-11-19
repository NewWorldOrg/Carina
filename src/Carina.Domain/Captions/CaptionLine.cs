namespace Carina.Domain.Captions;

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
