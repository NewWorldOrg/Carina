using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Api.Services;

public enum CatalogFailure
{
    NoSuchService = 1,

    NoSuchCandidate = 2,

    CandidateBelongsElsewhere = 3,

    NoTunerReceivesIt = 4,

    AlreadyKnown = 5,

    DriverUnreachable = 6,
}

public sealed record ServiceWithChannels(
    BroadcastService Service,
    IReadOnlyList<CandidateChannel> Candidates,
    StationLogoStamp? Logo = null,
    ServiceReception Reception = ServiceReception.Unknown);

public sealed class ChannelCatalogService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
    IStationLogoRepository logos,
    CandidateChannelDomainService additions,
    ITunerCapacityDirectory capacity,
    IAppEventPublisher events,
    IRecalculationNotice notices,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<ServiceWithChannels>>> ListAsync(
        CancellationToken cancellationToken)
    {
        List<ServiceWithChannels> listed = [];
        IReadOnlyList<StationLogoStamp> collected = await logos.StampsAsync(cancellationToken);
        TunerCapacity? inService = await capacity.ReadAsync(cancellationToken);

        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            IReadOnlyList<CandidateChannel> held = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                cancellationToken);

            listed.Add(new ServiceWithChannels(
                service,
                held,
                CollectedFor(service, collected),
                ServiceReceptions.Of(held, inService)));
        }

        return ServiceResult<IReadOnlyList<ServiceWithChannels>>.Success(listed);
    }

    public async Task<ServiceResult<ServiceWithChannels, CatalogFailure>> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { } service)
        {
            return Unknown(networkId, serviceId);
        }

        return ServiceResult<ServiceWithChannels, CatalogFailure>.Success(
            await GatherAsync(service, cancellationToken));
    }

    public async Task<ServiceResult<ServiceWithChannels, CatalogFailure>> SelectAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CandidateChannelId? candidateChannelId,
        CancellationToken cancellationToken)
    {
        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { } service)
        {
            return Unknown(networkId, serviceId);
        }

        if (candidateChannelId is null)
        {
            await candidates.ClearSelectionAsync(networkId, serviceId, cancellationToken);
            events.Signal(AppEventName.Tuners);
            notices.Nudge(RecalculationTrigger.SelectedChannelChanged);

            return ServiceResult<ServiceWithChannels, CatalogFailure>.Success(
                await GatherAsync(service, cancellationToken));
        }

        if (await candidates.FindAsync(candidateChannelId, cancellationToken) is not { } chosen)
        {
            return ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                $"No candidate channel called '{candidateChannelId.Value}' is held here.",
                CatalogFailure.NoSuchCandidate);
        }

        if (!chosen.BelongsTo(networkId, serviceId))
        {
            return ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                "That candidate channel belongs to another service, and a selection stays inside one.",
                CatalogFailure.CandidateBelongsElsewhere);
        }

        await candidates.SelectAsync(
            chosen.Id,
            SelectionSource.Manual,
            chosen.LastMeasurement,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken);
        events.Signal(AppEventName.Tuners);
        notices.Nudge(RecalculationTrigger.SelectedChannelChanged);

        return ServiceResult<ServiceWithChannels, CatalogFailure>.Success(
            await GatherAsync(service, cancellationToken));
    }

    public async Task<ServiceResult<ServiceWithChannels, CatalogFailure>> AddCandidateAsync(
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { } service)
        {
            return Unknown(networkId, serviceId);
        }

        CandidateChannelAdmission admission = await additions.AdmitAsync(networkId, serviceId, tuning, cancellationToken);

        if (admission.Added is not { } added)
        {
            return Refused(admission, tuning);
        }

        await candidates.AddAsync(added, cancellationToken);
        events.Signal(AppEventName.Tuners);

        return ServiceResult<ServiceWithChannels, CatalogFailure>.Success(
            await GatherAsync(service, cancellationToken));
    }

    public async Task<ServiceResult<ServiceWithChannels, CatalogFailure>> RemoveCandidateAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CandidateChannelId candidateChannelId,
        CancellationToken cancellationToken)
    {
        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { } service)
        {
            return Unknown(networkId, serviceId);
        }

        if (await candidates.FindAsync(candidateChannelId, cancellationToken) is not { } held)
        {
            return ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                $"No candidate channel called '{candidateChannelId.Value}' is held here.",
                CatalogFailure.NoSuchCandidate);
        }

        if (!held.BelongsTo(networkId, serviceId))
        {
            return ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                "That candidate channel belongs to another service.",
                CatalogFailure.CandidateBelongsElsewhere);
        }

        await candidates.RemoveAsync(candidateChannelId, cancellationToken);
        events.Signal(AppEventName.Tuners);
        notices.Nudge(RecalculationTrigger.SelectedChannelChanged);

        return ServiceResult<ServiceWithChannels, CatalogFailure>.Success(
            await GatherAsync(service, cancellationToken));
    }

    private static ServiceResult<ServiceWithChannels, CatalogFailure> Unknown(
        NetworkId networkId,
        ServiceId serviceId)
        => ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
            $"No service {networkId.Value}-{serviceId.Value} is held here.",
            CatalogFailure.NoSuchService);

    private static ServiceResult<ServiceWithChannels, CatalogFailure> Refused(
        CandidateChannelAdmission admission,
        TuningParameters tuning)
        => admission.Refusal switch
        {
            CandidateChannelRefusal.AlreadyHeld => ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                "This service already carries that channel as a candidate.",
                CatalogFailure.AlreadyKnown),
            CandidateChannelRefusal.DriverUnreachable => ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                "The driver could not be asked which tuners it holds, so this channel cannot be checked"
                + " against them; nothing was saved.",
                CatalogFailure.DriverUnreachable),
            _ => ServiceResult<ServiceWithChannels, CatalogFailure>.Failure(
                $"A {TuneSystemConverter.WireName(tuning.System)} channel needs a"
                + $" {TunerKindConverter.WireName(admission.Needed)} tuner in service, and this ledger holds"
                + $" {(admission.InService.Count == 0 ? "none" : string.Join(", ", admission.InService.Select(TunerKindConverter.WireName)))}.",
                CatalogFailure.NoTunerReceivesIt),
        };

    private static StationLogoStamp? CollectedFor(
        BroadcastService service,
        IReadOnlyList<StationLogoStamp> collected)
        => service.LogoId is { } named
            ? collected.FirstOrDefault(
                stamp => stamp.NetworkId.Equals(service.NetworkId) && stamp.LogoId.Equals(named))
            : null;

    private async Task<ServiceWithChannels> GatherAsync(
        BroadcastService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> found = await candidates.ListForServiceAsync(
            service.NetworkId,
            service.ServiceId,
            cancellationToken);

        return new ServiceWithChannels(
            service,
            found,
            CollectedFor(service, await logos.StampsAsync(cancellationToken)),
            ServiceReceptions.Of(found, await capacity.ReadAsync(cancellationToken)));
    }
}
