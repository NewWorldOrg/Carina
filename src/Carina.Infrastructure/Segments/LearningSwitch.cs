using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed class LearningSwitch(ISegmentSettingsRepository rows) : ILearningSwitch
{
    public async Task<bool> IsOnAsync(CancellationToken cancellationToken)
        => SegmentSettingsStanding.Over(await rows.ReadAsync(cancellationToken)).Learning;
}
