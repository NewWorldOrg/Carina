using Carina.Api.Common;
using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Services;

public enum LiveTicketRefusal
{
    NoSuchChannel = 1,

    TooManyOutstanding = 2,
}

public sealed record LiveChannelListing(
    BroadcastService Service,
    CandidateChannel Selected,
    IReadOnlyList<LiveSessionView> Sessions)
{
    public int Viewers => Sessions.Sum(session => session.Viewers);
}

public sealed class LiveService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
    ILiveSessionLedger sessions,
    IPlaybackTicketStore tickets)
{
    public static PlaybackTarget TargetOf(NetworkId network, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        return PlaybackTarget.LiveChannel($"{network.Value}-{service.Value}");
    }

    public async Task<ServiceResult<PaginatedList<LiveChannelListing>>> ListChannelsAsync(
        LiveChannelQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<LiveChannelListing> watchable = await WatchableAsync(cancellationToken);
        IReadOnlyList<LiveChannelListing> ordered = Ordered(watchable, query);

        return ServiceResult<PaginatedList<LiveChannelListing>>.Success(new PaginatedList<LiveChannelListing>(
            [.. ordered.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            ordered.Count,
            query.Page,
            query.PerPage));
    }

    public ServiceResult<IReadOnlyList<LiveProfile>> ListProfiles()
        => ServiceResult<IReadOnlyList<LiveProfile>>.Success(LiveProfile.All);

    public ServiceResult<IReadOnlyList<LiveSessionView>> ListSessions()
        => ServiceResult<IReadOnlyList<LiveSessionView>>.Success(
            [.. sessions.Running.OrderBy(session => session.Key.ToString(), StringComparer.Ordinal)]);

    public async Task<ServiceResult<IssuedPlaybackTicket, LiveTicketRefusal>> IssueTicketAsync(
        NetworkId network,
        ServiceId service,
        Subject watcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(watcher);

        if (await services.FindAsync(network, service, cancellationToken) is not { } held
            || !Watchable(held)
            || await candidates.FindSelectedAsync(network, service, cancellationToken) is null)
        {
            return ServiceResult<IssuedPlaybackTicket, LiveTicketRefusal>.Failure(
                $"No channel {network.Value}-{service.Value} can be watched live here.",
                LiveTicketRefusal.NoSuchChannel);
        }

        return tickets.Issue(watcher, TargetOf(network, service)) is { } issued
            ? ServiceResult<IssuedPlaybackTicket, LiveTicketRefusal>.Success(issued)
            : ServiceResult<IssuedPlaybackTicket, LiveTicketRefusal>.Failure(
                "Too many playback tickets are outstanding to issue another one now.",
                LiveTicketRefusal.TooManyOutstanding);
    }

    private static bool Watchable(BroadcastService service) => service.Category is ServiceCategory.Television;

    private static IReadOnlyList<LiveChannelListing> Ordered(IReadOnlyList<LiveChannelListing> listed, LiveChannelQuery query)
    {
        IOrderedEnumerable<LiveChannelListing> ordered = query.Sort switch
        {
            LiveChannelSort.Name => Directed(listed, listing => listing.Service.Name, query.Descending, StringComparer.Ordinal),
            LiveChannelSort.Viewers => Directed(listed, listing => listing.Viewers, query.Descending, Comparer<int>.Default),
            _ => Directed(listed, listing => listing.Service.RemoteControlKeyId ?? int.MaxValue, query.Descending, Comparer<int>.Default),
        };

        return
        [
            .. ordered
                .ThenBy(listing => listing.Service.NetworkId.Value)
                .ThenBy(listing => listing.Service.ServiceId.Value),
        ];
    }

    private static IOrderedEnumerable<LiveChannelListing> Directed<TKey>(
        IReadOnlyList<LiveChannelListing> listed,
        Func<LiveChannelListing, TKey> by,
        bool descending,
        IComparer<TKey> comparer)
        => descending ? listed.OrderByDescending(by, comparer) : listed.OrderBy(by, comparer);

    private async Task<IReadOnlyList<LiveChannelListing>> WatchableAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> selected = await candidates.ListSelectedAsync(cancellationToken);
        IReadOnlyList<LiveSessionView> running = sessions.Running;
        List<LiveChannelListing> listed = [];

        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            if (!Watchable(service))
            {
                continue;
            }

            CandidateChannel? tuned = selected.FirstOrDefault(candidate =>
                candidate.NetworkId.Equals(service.NetworkId) && candidate.ServiceId.Equals(service.ServiceId));

            if (tuned is null)
            {
                continue;
            }

            listed.Add(new LiveChannelListing(
                service,
                tuned,
                [.. running.Where(session =>
                    session.Key.Network.Equals(service.NetworkId) && session.Key.Service.Equals(service.ServiceId))]));
        }

        return listed;
    }
}
