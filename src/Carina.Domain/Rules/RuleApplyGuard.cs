namespace Carina.Domain.Rules;

public enum RuleApplyRefusal
{
    None = 0,

    OneIsAlreadyRunning = 1,

    TooSoonAfterTheLastOne = 2,

    ARecalculationIsAlreadyRunning = 3,
}

public sealed record RuleApplyVerdict(RuleApplyRefusal Refusal, Guid? RunningId, DateTime? NotBefore)
{
    public static readonly RuleApplyVerdict Allowed = new(RuleApplyRefusal.None, null, null);

    public bool IsAllowed => Refusal is RuleApplyRefusal.None;
}

public static class RuleApplyGuard
{
    public static RuleApplyVerdict Of(
        Guid? running,
        DateTime? lastFinishedAt,
        DateTime now,
        TimeSpan cooldown)
    {
        if (running is { } id)
        {
            return new RuleApplyVerdict(RuleApplyRefusal.OneIsAlreadyRunning, id, null);
        }

        if (lastFinishedAt is not { } finished)
        {
            return RuleApplyVerdict.Allowed;
        }

        DateTime notBefore = finished + cooldown;

        return now < notBefore
            ? new RuleApplyVerdict(RuleApplyRefusal.TooSoonAfterTheLastOne, null, notBefore)
            : RuleApplyVerdict.Allowed;
    }
}
