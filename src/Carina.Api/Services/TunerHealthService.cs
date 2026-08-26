using Carina.Api.Common;
using Carina.Domain.Channels;

namespace Carina.Api.Services;

public enum TunerHealthFailure
{
    CapacityUnknown,
    OutOfRange,
}

public sealed record TunerHealthView(
    IReadOnlyList<SystemReach> Systems,
    int HoursOfSilence,
    IReadOnlyList<string> Undetermined);

public sealed class TunerHealthService(
    ITunerCapacityDirectory capacity,
    ICandidateChannelRepository candidates,
    IServiceReachSettingsRepository settings,
    TimeProvider clock)
{
    public async Task<ServiceResult<TunerHealthView, TunerHealthFailure>> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (await capacity.ReadAsync(cancellationToken) is not { } reachable)
        {
            return ServiceResult<TunerHealthView, TunerHealthFailure>.Failure(
                "The tuner ledger could not be read, so how far the broadcast reaches is unknown rather than nothing.",
                TunerHealthFailure.CapacityUnknown);
        }

        ServiceReachSettings held = await settings.ReadAsync(cancellationToken);
        IReadOnlyList<CandidateChannel> known = await candidates.ListAllAsync(cancellationToken);

        return ServiceResult<TunerHealthView, TunerHealthFailure>.Success(
            new TunerHealthView(
                ServiceReach.Assess(reachable.Served, known, held.Silence, clock.GetUtcNow().UtcDateTime),
                held.HoursOfSilence,
                reachable.Undetermined));
    }

    public async Task<ServiceResult<int, TunerHealthFailure>> AllowSilenceForAsync(
        int hoursOfSilence,
        CancellationToken cancellationToken)
    {
        if (hoursOfSilence < ServiceReachSettings.ShortestHoursOfSilence
            || hoursOfSilence > ServiceReachSettings.LongestHoursOfSilence)
        {
            return ServiceResult<int, TunerHealthFailure>.Failure(
                $"hoursOfSilence: expected {ServiceReachSettings.ShortestHoursOfSilence}"
                + $" to {ServiceReachSettings.LongestHoursOfSilence}; got {hoursOfSilence}.",
                TunerHealthFailure.OutOfRange);
        }

        ServiceReachSettings held = await settings.ReadAsync(cancellationToken);
        held.AllowSilenceFor(hoursOfSilence, clock.GetUtcNow().UtcDateTime);
        await settings.SaveAsync(held, cancellationToken);

        return ServiceResult<int, TunerHealthFailure>.Success(held.HoursOfSilence);
    }
}
