using Carina.Domain.Reservations;

namespace Carina.TestSupport;

public sealed class CountedNotices : IRecalculationNotice
{
    public List<RecalculationTrigger> Nudged { get; } = [];

    public void Nudge(RecalculationTrigger trigger) => Nudged.Add(trigger);
}
