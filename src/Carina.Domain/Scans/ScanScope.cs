using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

public sealed record ScanScope
{
    private ScanScope(IReadOnlyList<TuneSystem> systems, IReadOnlyList<TuningParameters> namedTargets)
    {
        Systems = systems;
        NamedTargets = namedTargets;
    }

    public static ScanScope Everything { get; } =
        Of(TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110);

    public IReadOnlyList<TuneSystem> Systems { get; }

    public IReadOnlyList<TuningParameters> NamedTargets { get; }

    public bool NamesItsOwnTargets => NamedTargets.Count > 0;

    public static ScanScope Of(params TuneSystem[] systems)
    {
        ArgumentNullException.ThrowIfNull(systems);

        if (systems.Contains(TuneSystem.Unspecified))
        {
            throw new ArgumentException(
                "A scan covers systems it can name; Unspecified is not one of them.",
                nameof(systems));
        }

        var wanted = systems.Distinct().ToArray();

        if (wanted.Length == 0)
        {
            throw new ArgumentException("A scan covers at least one system.", nameof(systems));
        }

        return new ScanScope(wanted, []);
    }

    public static ScanScope Over(IReadOnlyList<TuningParameters> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var named = targets.Distinct().ToArray();

        if (named.Length == 0)
        {
            throw new ArgumentException("A scan over named targets names at least one.", nameof(targets));
        }

        return new ScanScope([.. named.Select(target => target.System).Distinct()], named);
    }

    public bool Covers(TuneSystem system) => Systems.Contains(system);
}
