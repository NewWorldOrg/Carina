using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Reads and changes the segment settings changed from a screen. A change names the threshold a skip button is
/// offered from, and is made against the settings as they stand once no other change is being made.
/// </summary>
public sealed class SegmentSettingsService(
    ISegmentSettingsRepository rows,
    IAtomicWrite writes,
    TimeProvider clock)
{
    private const string SkipThresholdExpected =
        "skipThreshold: expected how likely a stretch is before a button offers to skip it, from 0.50 to 0.95.";

    public async Task<ServiceResult<SegmentSettingsStanding>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<SegmentSettingsStanding>.Success(await StandingAsync(cancellationToken));

    public async Task<ServiceResult<SegmentSettingsStanding>> ChangeAsync(double? skipThreshold, CancellationToken cancellationToken)
    {
        if (skipThreshold is not double asked || SegmentSettingsStanding.SkipThresholdOf(asked) is not double threshold)
        {
            return ServiceResult<SegmentSettingsStanding>.Failure(SkipThresholdExpected);
        }

        SegmentSettingsStanding changed = await writes.AllOrNothingAsync(
            async token =>
            {
                await rows.TakeTurnAsync(token);

                SegmentSettingsStanding standing = await StandingAsync(token);

                if (standing.Set(threshold, clock.GetUtcNow().UtcDateTime) is not { } set)
                {
                    return standing;
                }

                await rows.SaveAsync(set, token);

                return SegmentSettingsStanding.Over(set);
            },
            cancellationToken);

        return ServiceResult<SegmentSettingsStanding>.Success(changed);
    }

    private async Task<SegmentSettingsStanding> StandingAsync(CancellationToken cancellationToken)
        => SegmentSettingsStanding.Over(await rows.ReadAsync(cancellationToken));
}
