using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

public sealed record ScanPlan(ScanScope Scope, IReadOnlyList<TuningParameters> Targets);
