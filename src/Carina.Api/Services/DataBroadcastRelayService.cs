using System.Text.RegularExpressions;

using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Auth;
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

    NoSuchApplication = 5,

    TooManyOutstanding = 6,

    AitNotRead = 7,

    OutsideTheBoundary = 8,
}

/// <summary>
/// What a viewer asks to start a Hybridcast application by: the address of the AIT, the organisation and application
/// identifiers the document named, and the path of the receiver script the application's documents are given.
/// </summary>
public sealed record HybridcastAsk(string Ait, uint OrgId, ushort AppId, string ReceiverScript);

/// <summary>
/// A Hybridcast application started for a viewer: the ticket its frame opens the relay with, and the application.
/// </summary>
public sealed record HybridcastStart(IssuedPlaybackTicket Ticket, HybridcastApplication Application);

/// <summary>
/// Relays a live channel's data broadcast document to a broadcaster's server, only where the browser asking allows
/// communication and only for a channel that can be watched live here, and names the receiver's card to the document
/// only where the browser lets that be handed over too. Where the browser allows Hybridcast as well, it reads the AIT
/// a document names and starts the application the document asks for, handing the viewer a ticket its frame opens
/// the relay with; what the frame asks for is relayed only inside the application's boundary.
/// </summary>
public sealed partial class DataBroadcastRelayService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
    IBroadcasterRelay relay,
    IConditionalAccessCardReader cards,
    IHybridcastRelay hybridcast,
    IHybridcastLaunches launches,
    IPlaybackTicketStore tickets)
{
    public const string CommunicationIsOff =
        "Communication for data broadcasts is off in this browser's viewing settings.";

    public const string ReceiverIdIsKept =
        "Handing the receiver's identifier to data broadcasts is off in this browser's viewing settings.";

    public const string NoCardHasAnswered =
        "No conditional-access card has answered the driver, so there is no identifier to hand over.";

    public const string DriverOutOfReach =
        "The driver could not be asked which card answered it.";

    public const string HybridcastIsOff =
        "Hybridcast is off in this browser's viewing settings.";

    public const string NoAitWasRead =
        "The broadcaster's server did not hand over an AIT that could be read.";

    public const string NothingToStart =
        "The AIT names no application to start by these identifiers.";

    public const string TooManyStarted =
        "Too many playback tickets are outstanding to start another application now.";

    public const string NotThisApplication =
        "This ticket started no application on this channel.";

    public const string OutsideTheBoundaryOfTheApplication =
        "The address is outside the boundary of the application.";

    public static PlaybackTarget ApplicationTargetOf(NetworkId network, ServiceId service)
        => PlaybackTarget.HybridcastApplication(ChannelOf(network, service));

    public static bool IsReceiverScriptPath(string? path)
        => path is not null
           && ReceiverScriptPath().IsMatch(path)
           && !path.Contains("//", StringComparison.Ordinal)
           && !path.Contains("..", StringComparison.Ordinal);

    /// <summary>
    /// The applications the AIT at an address names, for a document of a live channel: only where the browser allows
    /// both communication and Hybridcast.
    /// </summary>
    public async Task<ServiceResult<RelayedAit, DataBroadcastRelayRefusal>> ReadAitAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        bool hybridcastAllowed,
        string uri,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(network, service, allowed, hybridcastAllowed, cancellationToken) is { } refused)
        {
            return ServiceResult<RelayedAit, DataBroadcastRelayRefusal>.Failure(refused.Said, refused.Refusal);
        }

        RelayedAit read = await hybridcast.ReadAitAsync(ChannelOf(network, service), uri, cancellationToken);

        return ServiceResult<RelayedAit, DataBroadcastRelayRefusal>.Success(read);
    }

    /// <summary>
    /// Reads the AIT again and starts the application the identifiers name, where it is one to start by itself:
    /// a ticket is issued to the viewer for the application on this channel, and the application is kept under it.
    /// </summary>
    public async Task<ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>> StartApplicationAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        bool hybridcastAllowed,
        Subject viewer,
        HybridcastAsk ask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(ask);

        if (await RefusedAsync(network, service, allowed, hybridcastAllowed, cancellationToken) is { } refused)
        {
            return ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>.Failure(refused.Said, refused.Refusal);
        }

        string channel = ChannelOf(network, service);
        RelayedAit read = await hybridcast.ReadAitAsync(channel, ask.Ait, cancellationToken);

        if (read.Applications is not { } applications)
        {
            return ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>.Failure(NoAitWasRead, DataBroadcastRelayRefusal.AitNotRead);
        }

        if (applications.FirstOrDefault(application => application.IsStartedBy(ask.OrgId, ask.AppId)) is not { } started)
        {
            return ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>.Failure(NothingToStart, DataBroadcastRelayRefusal.NoSuchApplication);
        }

        if (tickets.Issue(viewer, ApplicationTargetOf(network, service)) is not { } issued)
        {
            return ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>.Failure(TooManyStarted, DataBroadcastRelayRefusal.TooManyOutstanding);
        }

        launches.Keep(issued.InTheClear, new HybridcastLaunch(channel, viewer, started.EntryUrl, started.Reach(), ask.ReceiverScript));

        return ServiceResult<HybridcastStart, DataBroadcastRelayRefusal>.Success(new HybridcastStart(issued, started));
    }

    /// <summary>
    /// The application a frame's ticket started, where it was started on this channel by the same viewer.
    /// </summary>
    public ServiceResult<HybridcastLaunch, DataBroadcastRelayRefusal> LaunchFor(NetworkId network, ServiceId service, string? carrier, Subject viewer)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(viewer);

        return launches.Find(carrier) is { } launch && launch.Channel == ChannelOf(network, service) && launch.Subject.Equals(viewer)
            ? ServiceResult<HybridcastLaunch, DataBroadcastRelayRefusal>.Success(launch)
            : ServiceResult<HybridcastLaunch, DataBroadcastRelayRefusal>.Failure(NotThisApplication, DataBroadcastRelayRefusal.NotAllowed);
    }

    public async Task<ServiceResult<RelayedBody, DataBroadcastRelayRefusal>> FetchApplicationAsync(
        HybridcastLaunch launch,
        Uri address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(address);

        if (!launch.Boundary.Admits(address))
        {
            return ServiceResult<RelayedBody, DataBroadcastRelayRefusal>.Failure(
                OutsideTheBoundaryOfTheApplication,
                DataBroadcastRelayRefusal.OutsideTheBoundary);
        }

        RelayedBody fetched = await hybridcast.FetchAsync(launch.Channel, address, launch.Boundary, cancellationToken);

        return ServiceResult<RelayedBody, DataBroadcastRelayRefusal>.Success(fetched);
    }

    public async Task<ServiceResult<RelayedSocket, DataBroadcastRelayRefusal>> OpenApplicationSocketAsync(
        HybridcastLaunch launch,
        Uri address,
        IReadOnlyList<string> subProtocols,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(address);

        if (!launch.Boundary.Admits(address))
        {
            return ServiceResult<RelayedSocket, DataBroadcastRelayRefusal>.Failure(
                OutsideTheBoundaryOfTheApplication,
                DataBroadcastRelayRefusal.OutsideTheBoundary);
        }

        RelayedSocket opened = await hybridcast.OpenSocketAsync(launch.Channel, address, launch.Boundary, subProtocols, cancellationToken);

        return ServiceResult<RelayedSocket, DataBroadcastRelayRefusal>.Success(opened);
    }

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

    [GeneratedRegex("^/[A-Za-z0-9._~/-]{1,255}$")]
    private static partial Regex ReceiverScriptPath();

    private async Task<Refused?> RefusedAsync(
        NetworkId network,
        ServiceId service,
        bool allowed,
        bool hybridcastAllowed,
        CancellationToken cancellationToken)
    {
        if (allowed && !hybridcastAllowed)
        {
            return new Refused(HybridcastIsOff, DataBroadcastRelayRefusal.NotAllowed);
        }

        return await RefusedAsync(network, service, allowed, cancellationToken);
    }

    private sealed record Refused(string Said, DataBroadcastRelayRefusal Refusal);
}
