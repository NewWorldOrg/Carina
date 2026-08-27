namespace Carina.Domain.Integrity;

public enum SweepRefusal
{
    None = 0,

    OneIsAlreadyRunning = 1,

    TooSoonAfterTheLastOne = 2,
}

public sealed record SweepVerdict(SweepRefusal Refusal, IntegrityCheckId? RunningId, DateTime? NotBefore)
{
    public static readonly SweepVerdict Allowed = new(SweepRefusal.None, null, null);

    public bool IsAllowed => Refusal is SweepRefusal.None;
}

public static class SweepGuard
{
    public static SweepVerdict Of(
        IntegrityCheckId? running,
        DateTime? lastFinishedAt,
        DateTime now,
        TimeSpan cooldown)
    {
        if (running is { } walking)
        {
            return new SweepVerdict(SweepRefusal.OneIsAlreadyRunning, walking, null);
        }

        if (lastFinishedAt is not { } finished)
        {
            return SweepVerdict.Allowed;
        }

        DateTime notBefore = finished + cooldown;

        return now < notBefore
            ? new SweepVerdict(SweepRefusal.TooSoonAfterTheLastOne, null, notBefore)
            : SweepVerdict.Allowed;
    }
}
