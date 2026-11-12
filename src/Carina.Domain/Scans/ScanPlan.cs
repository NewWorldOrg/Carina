using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

/// <summary>
/// The part of a scope a tuner in service can receive, and the targets walked for it, in order.
/// </summary>
public sealed record ScanPlan(ScanScope Scope, IReadOnlyList<TuningParameters> Targets);
