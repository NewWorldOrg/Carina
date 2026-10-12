using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Driver;

namespace Carina.Api.Services;

public enum DataBroadcastRelayRefusal
{
    NotAllowed = 1,

    NoSuchChannel = 2,

    NoCard = 3,

    DriverUnreachable = 4,
}

/// <summary>
/// Relays a live channel's data broadcast document to a broadcaster's server, only where the browser asking allows
/// communication and only for a channel that can be watched live here, and names the receiver's card to the document
/// only where the browser lets that be handed over too.
/// </summary>
public sealed class DataBroadcastRelayService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
    IBroadcasterRelay relay,
    IConditionalAccessCardReader cards)
{
    public const string CommunicationIsOff =
        "Communication for data broadcasts is off in this browser's viewing settings.";

    public const string ReceiverIdIsKept =
        "Handing the receiver's identifier to data broadcasts is off in this browser's viewing settings.";

    public const string NoCardHasAnswered =
        "No conditional-access card has answered the driver, so there is no identifier to hand over.";

    public const string DriverOutOfReach =
        "The driver could not be asked which card answered it.";

    /// <summary>
    /// The card the driver last heard from, for a document of a live channel to be given as the receiver's
    /// identifier: only where the browser allows communication and hands the identifier over.
    /// </summary>
    public async Task<ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>> ReceiverIdAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        bool handed,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(network, service, allowed, cancellationToken) is { } refused)
        {
            return ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>.Failure(refused.Said, refused.Refusal);
        }

        if (!handed)
        {
            return ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>.Failure(
                ReceiverIdIsKept,
                DataBroadcastRelayRefusal.NotAllowed);
        }

        DriverCall<ConditionalAccessCardDto> asked = await cards.GetConditionalAccessCardAsync(cancellationToken);

        if (asked.TryGetValue(out ConditionalAccessCardDto? card))
        {
            return ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>.Success(card);
        }

        return asked.Outcome is DriverCallOutcome.Unreachable
            ? ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>.Failure(
                DriverOutOfReach,
                DataBroadcastRelayRefusal.DriverUnreachable)
            : ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal>.Failure(
                NoCardHasAnswered,
                DataBroadcastRelayRefusal.NoCard);
    }

    public async Task<ServiceResult<RelayedText, DataBroadcastRelayRefusal>> TransmitAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        string uri,
        string text,
        string charset,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(network, service, allowed, cancellationToken) is { } refused)
        {
            return ServiceResult<RelayedText, DataBroadcastRelayRefusal>.Failure(refused.Said, refused.Refusal);
        }

        RelayedText relayed = await relay.TransmitAsync(ChannelOf(network, service), uri, text, charset, cancellationToken);

        return ServiceResult<RelayedText, DataBroadcastRelayRefusal>.Success(relayed);
    }

    public async Task<ServiceResult<RelayedResource, DataBroadcastRelayRefusal>> FetchAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        string uri,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(network, service, allowed, cancellationToken) is { } refused)
        {
            return ServiceResult<RelayedResource, DataBroadcastRelayRefusal>.Failure(refused.Said, refused.Refusal);
        }

        RelayedResource relayed = await relay.FetchAsync(ChannelOf(network, service), uri, cancellationToken);

        return ServiceResult<RelayedResource, DataBroadcastRelayRefusal>.Success(relayed);
    }

    private static string ChannelOf(NetworkId network, ServiceId service) => $"{network.Value}-{service.Value}";

    private async Task<Refused?> RefusedAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        if (!allowed)
        {
            return new Refused(CommunicationIsOff, DataBroadcastRelayRefusal.NotAllowed);
        }

        if (await services.FindAsync(network, service, cancellationToken) is not { Category: ServiceCategory.Television }
            || await candidates.FindSelectedAsync(network, service, cancellationToken) is null)
        {
            return new Refused(
                $"No channel {network.Value}-{service.Value} can be watched live here.",
                DataBroadcastRelayRefusal.NoSuchChannel);
        }

        return null;
    }

    private sealed record Refused(string Said, DataBroadcastRelayRefusal Refusal);
}
