using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Reads and changes the segment settings changed from a screen. A change names only what it changes.
/// </summary>
public sealed class SegmentSettingsService(ISegmentSettingsRepository rows, TimeProvider clock)
{
    public async Task<ServiceResult<SegmentSettingsStanding>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<SegmentSettingsStanding>.Success(await StandingAsync(cancellationToken));

    public async Task<ServiceResult<SegmentSettingsStanding>> ChangeAsync(
        bool? learning,
        CancellationToken cancellationToken)
    {
        if (learning is not { } on)
        {
            return ServiceResult<SegmentSettingsStanding>.Failure(
                "learning: expected true to turn CM, OP and ED learning on or false to turn it off, "
                + "the one setting a change can name.");
        }

        SegmentSettingsStanding standing = await StandingAsync(cancellationToken);

        if (standing.Learning == on)
        {
            return ServiceResult<SegmentSettingsStanding>.Success(standing);
        }

        await rows.SaveAsync(SegmentSettings.LearningSwitched(on, clock.GetUtcNow().UtcDateTime), cancellationToken);

        return ServiceResult<SegmentSettingsStanding>.Success(await StandingAsync(cancellationToken));
    }

    private async Task<SegmentSettingsStanding> StandingAsync(CancellationToken cancellationToken)
        => SegmentSettingsStanding.Over(await rows.ReadAsync(cancellationToken));
}
