using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

namespace Carina.Driver;

public sealed record DriverShutdownBudget(TimeSpan Drain, TimeSpan HardStop, TimeSpan HostSlack)
{
    public static readonly TimeSpan DefaultHostSlack = TimeSpan.FromMinutes(1);

    public static DriverShutdownBudget From(DriverConfiguration configuration) =>
        new(
            TimeSpan.FromHours(Math.Max(0, configuration.ShutdownGraceHours)),
            TunerSessionManager.DefaultHardStopLimit,
            DefaultHostSlack
        );

    public TimeSpan Total => Drain + HardStop + HostSlack;

    public long TotalSeconds => (long)Total.TotalSeconds;

    public string Describe() =>
        $"shutdown budget {TotalSeconds}s = recording linger {(long)Drain.TotalSeconds}s "
        + $"+ hard stop {(long)HardStop.TotalSeconds}s + host slack {(long)HostSlack.TotalSeconds}s. "
        + $"The runtime has to allow at least {TotalSeconds}s before it sends SIGKILL.";
}
