using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class LoginTries
{
    private readonly List<DateTime> taken = [];

    public DateTime? Take(DateTime now, LoginRatePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(now, nameof(now));

        DateTime edge = now - policy.Window;

        taken.RemoveAll(moment => moment <= edge);

        if (taken.Count >= policy.FailuresBeforeRefusing)
        {
            return taken[0] + policy.Window;
        }

        taken.Add(now);

        return null;
    }
}
