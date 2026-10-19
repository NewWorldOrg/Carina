using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Reads and changes the segment settings changed from a screen. A change names only what it changes, and is
/// made against the settings as they stand once no other change is being made. A change asking for learning to be
/// on, once it is, has the version of four kinds made by hand take the place of the first, through the one change
/// that moves the version in use and only while the first is in use as it was installed; the migration that made
/// the version leaves it a candidate while learning is off.
/// </summary>
public sealed class SegmentSettingsService(
    ISegmentSettingsRepository rows,
    IAtomicWrite writes,
    ISegmentModelVersionRepository versions,
    TimeProvider clock)
{
    public async Task<ServiceResult<SegmentSettingsStanding>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<SegmentSettingsStanding>.Success(await StandingAsync(cancellationToken));

    public async Task<ServiceResult<SegmentSettingsStanding>> ChangeAsync(
        bool? learning,
        double? skipThreshold,
        CancellationToken cancellationToken)
    {
        if (learning is null && skipThreshold is null)
        {
            return ServiceResult<SegmentSettingsStanding>.Failure(
                "learning: expected true to turn CM, OP and ED learning on or false to turn it off; "
                + "skipThreshold: expected how likely a stretch is before a button offers to skip it, from 0.50 to 0.95. "
                + "A change names one of the two, or both.");
        }

        double? threshold = skipThreshold is double asked ? SegmentSettingsStanding.SkipThresholdOf(asked) : null;

        if (skipThreshold is not null && threshold is null)
        {
            return ServiceResult<SegmentSettingsStanding>.Failure(
                "skipThreshold: expected how likely a stretch is before a button offers to skip it, from 0.50 to 0.95.");
        }

        SegmentSettingsStanding changed = await writes.AllOrNothingAsync(
            async token =>
            {
                await rows.TakeTurnAsync(token);

                SegmentSettingsStanding standing = await StandingAsync(token);

                if (standing.Set(learning, threshold, clock.GetUtcNow().UtcDateTime) is not { } set)
                {
                    return standing;
                }

                await rows.SaveAsync(set, token);

                return SegmentSettingsStanding.Over(set);
            },
            cancellationToken);

        if (learning is true && changed.Learning)
        {
            await versions.ChangeAsync(SegmentVersionChange.FourKindsInPlaceOfTheFirst, clock.GetUtcNow().UtcDateTime, cancellationToken);
        }

        return ServiceResult<SegmentSettingsStanding>.Success(changed);
    }

    private async Task<SegmentSettingsStanding> StandingAsync(CancellationToken cancellationToken)
        => SegmentSettingsStanding.Over(await rows.ReadAsync(cancellationToken));
}
