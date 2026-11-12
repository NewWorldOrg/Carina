using Carina.Domain.Base;

namespace Carina.Domain.Auth;

/// <summary>
/// The sign-in tries one caller made within the window of a <see cref="LoginRatePolicy"/>. A try is counted before
/// its password is checked; one that passes is followed by forgetting the caller altogether. It is not safe for
/// two threads at once: whoever holds it takes the lock.
/// </summary>
public sealed class LoginTries
{
    private readonly List<DateTime> taken = [];

    /// <summary>
    /// Counts a try at <paramref name="now"/> and answers nothing, or answers the moment the oldest try in the window
    /// leaves it when the policy's tries are already spent, counting nothing.
    /// </summary>
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
