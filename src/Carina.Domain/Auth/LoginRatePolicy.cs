namespace Carina.Domain.Auth;

public sealed record LoginRatePolicy
{
    public LoginRatePolicy(int failuresBeforeRefusing, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failuresBeforeRefusing, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        FailuresBeforeRefusing = failuresBeforeRefusing;
        Window = window;
    }

    public static LoginRatePolicy Default { get; } = new(5, TimeSpan.FromMinutes(5));

    public int FailuresBeforeRefusing { get; }

    public TimeSpan Window { get; }
}
