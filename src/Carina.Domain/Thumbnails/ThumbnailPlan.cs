using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public enum ThumbnailIntent
{
    Draw = 1,

    Skip = 2,
}

public sealed record ThumbnailPlan
{
    private ThumbnailPlan(ThumbnailIntent intent, TimeSpan at, bool ofSomethingUnfinished)
    {
        Intent = intent;
        At = at;
        OfSomethingUnfinished = ofSomethingUnfinished;
    }

    public ThumbnailIntent Intent { get; }

    public TimeSpan At { get; }

    public bool OfSomethingUnfinished { get; }

    public static ThumbnailPlan For(ThumbnailSubject subject, ThumbnailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(settings);

        return subject.Outcome is RecordingOutcome.Failed
            ? new ThumbnailPlan(ThumbnailIntent.Skip, TimeSpan.Zero, ofSomethingUnfinished: false)
            : new ThumbnailPlan(
                ThumbnailIntent.Draw,
                settings.PositionIn(subject.Written),
                subject.Outcome is RecordingOutcome.Truncated);
    }
}
