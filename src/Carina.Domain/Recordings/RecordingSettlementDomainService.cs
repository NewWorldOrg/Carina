using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Recordings;

/// <summary>
/// What a recording is judged against as it ends: the rate its file is weighed against and the quality levels in
/// force.
/// </summary>
public sealed class RecordingSettlementDomainService(
    IServiceTuningDirectory directory,
    IQualityThresholdRepository thresholds)
{
    public async Task<SettlementRate> RateAsync(Recording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        TuningResolution resolution = await directory.ResolveTuningAsync(
            recording.NetworkId,
            recording.ServiceId,
            cancellationToken);

        if (resolution.ChannelTuning is { } tuning)
        {
            return new SettlementRate(ExpectedBitrate.Of(tuning.Typed().Kind), resolution.Refusal, false);
        }

        return resolution.Refusal is TuningRefusal.NoSuchService or TuningRefusal.NoSelectedChannel
            ? new SettlementRate(ExpectedBitrate.OfAnyKind, resolution.Refusal, true)
            : new SettlementRate(null, resolution.Refusal, false);
    }

    public async Task<QualityBands> BandsAsync(DateTime at, CancellationToken cancellationToken)
        => QualityThresholdStanding.Bands(
            QualityThresholdStanding.Over(await thresholds.ListAsync(cancellationToken), at));
}
