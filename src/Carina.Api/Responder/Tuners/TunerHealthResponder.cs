using Carina.Api.Services;
using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Api.Responder.Tuners;

public sealed record SystemReachResponder(
    TuneSystem System,
    ServiceReachLevel Level,
    int Services,
    DateTime? LastSeenAt)
{
    public static SystemReachResponder Of(SystemReach reach)
    {
        ArgumentNullException.ThrowIfNull(reach);

        return new SystemReachResponder(reach.System, reach.Level, reach.Services, reach.LastSeenAt);
    }
}

public sealed record TunerHealthResponder(
    IReadOnlyList<SystemReachResponder> Systems,
    int HoursOfSilence,
    IReadOnlyList<string> Undetermined)
{
    public static TunerHealthResponder Of(TunerHealthView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new TunerHealthResponder(
            [.. view.Systems.Select(SystemReachResponder.Of)],
            view.HoursOfSilence,
            view.Undetermined);
    }
}

public sealed record ServiceReachSettingsResponder(int HoursOfSilence)
{
    public static ServiceReachSettingsResponder Of(int hoursOfSilence) => new(hoursOfSilence);
}
