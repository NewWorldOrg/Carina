namespace Carina.Domain.Programmes;

public enum BoostRefusal
{
    None = 0,

    OneIsAlreadyRunning = 1,

    TooSoonAfterTheLastOne = 2,
}

public sealed record BoostVerdict(BoostRefusal Refusal, Guid? RunningId, DateTime? NotBefore)
{
    public bool IsAllowed => Refusal is BoostRefusal.None;

    public static readonly BoostVerdict Allowed = new(BoostRefusal.None, null, null);
}

public static class BoostGuard
{
    public static BoostVerdict Of(
        Guid? running,
        DateTime? lastFinishedAt,
        DateTime now,
        TimeSpan cooldown)
    {
        if (running is { } id)
        {
            return new BoostVerdict(BoostRefusal.OneIsAlreadyRunning, id, null);
        }

        if (lastFinishedAt is not { } finished)
        {
            return BoostVerdict.Allowed;
        }

        DateTime notBefore = finished + cooldown;

        return now < notBefore
            ? new BoostVerdict(BoostRefusal.TooSoonAfterTheLastOne, null, notBefore)
            : BoostVerdict.Allowed;
    }
}
