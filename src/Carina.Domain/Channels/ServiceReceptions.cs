using Puppis.Contracts;

namespace Carina.Domain.Channels;

public static class ServiceReceptions
{
    public static ServiceReception Of(IReadOnlyList<CandidateChannel> candidates, TunerCapacity? capacity)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (capacity is null || capacity.Undetermined.Count > 0)
        {
            return ServiceReception.Unknown;
        }

        IReadOnlyList<TuneSystem> systems = candidates.FirstOrDefault(candidate => candidate.IsSelected) is { } selected
            ? [selected.Tuning.System]
            : [.. candidates.Select(candidate => candidate.Tuning.System).Distinct()];

        if (systems.Count is 0)
        {
            return ServiceReception.Unknown;
        }

        return systems.Any(capacity.CanServe)
            ? ServiceReception.Receivable
            : ServiceReception.NoTunerInService;
    }
}
