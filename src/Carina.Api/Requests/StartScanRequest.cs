using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Api.Requests;

public sealed record StartScanRequest
{
    public IReadOnlyList<TuneSystem>? Systems { get; init; }

    public IReadOnlyList<TuningParametersRequest>? Channels { get; init; }

    public ScanScope? ToScope(out string? problem)
    {
        problem = null;

        if (Channels is { Count: > 0 } named)
        {
            var targets = new List<TuningParameters>(named.Count);

            foreach (var channel in named)
            {
                if (channel.ToParameters(out var refusal) is not { } tuning)
                {
                    problem = $"channels: {refusal}";

                    return null;
                }

                targets.Add(tuning);
            }

            return ScanScope.Over(targets);
        }

        if (Systems is not { Count: > 0 } wanted)
        {
            return ScanScope.Everything;
        }

        if (wanted.Contains(TuneSystem.Unspecified))
        {
            problem = "systems: expected isdbT, isdbSBs or isdbSCs110; a scan covers systems it can name.";

            return null;
        }

        return ScanScope.Of([.. wanted]);
    }
}
