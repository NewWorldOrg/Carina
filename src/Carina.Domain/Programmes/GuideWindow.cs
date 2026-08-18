using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public sealed class GuideWindow
{
    public static readonly TimeSpan Longest = TimeSpan.FromDays(2);

    private GuideWindow(DateTime from, DateTime to)
    {
        From = from;
        To = to;
    }

    public DateTime From { get; }

    public DateTime To { get; }

    public static GuideWindow? Between(DateTime from, DateTime to)
    {
        if (from.Kind is not DateTimeKind.Utc || to.Kind is not DateTimeKind.Utc)
        {
            return null;
        }

        if (to <= from || to - from > Longest)
        {
            return null;
        }

        return new GuideWindow(
            UtcTimes.Required(from, nameof(from)),
            UtcTimes.Required(to, nameof(to)));
    }
}
