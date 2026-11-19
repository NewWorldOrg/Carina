namespace Carina.Domain.Captions;

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
