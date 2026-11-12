using Carina.Domain.Channels;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Channels;

public sealed class LiveChannelDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private readonly HeldServices services = new();

    private readonly HeldCandidates candidates = new();

    private LiveChannelDomainService Service => new(services, candidates);

    [Fact(DisplayName = "a television service whose channel is selected is watched live, through that channel")]
    public async Task ATelevisionServiceWithASelectedChannelIsWatchedLive()
    {
        BroadcastService held = Held(101, ServiceCategory.Television);
        CandidateChannel selected = Candidate(101, 27, selected: true);
        Candidate(101, 28, selected: false);

        WatchableChannel? found = await Service.FindAsync(held.NetworkId, held.ServiceId, Cancel);

        Assert.NotNull(found);
        Assert.Same(held, found.Service);
        Assert.Same(selected, found.Selected);
    }

    [Fact(DisplayName = "a service not held, not television, or with no channel selected is not watched live")]
    public async Task OtherServicesAreNotWatchedLive()
    {
        Held(101, ServiceCategory.Television);
        Candidate(101, 27, selected: false);
        Held(102, ServiceCategory.Radio);
        Candidate(102, 27, selected: true);

        Assert.Null(await Service.FindAsync(new NetworkId(4), new ServiceId(101), Cancel));
        Assert.Null(await Service.FindAsync(new NetworkId(4), new ServiceId(102), Cancel));
        Assert.Null(await Service.FindAsync(new NetworkId(4), new ServiceId(103), Cancel));
    }

    [Fact(DisplayName = "the services watched live are listed by the same condition, each with its selected channel")]
    public async Task TheListUsesTheSameCondition()
    {
        BroadcastService television = Held(101, ServiceCategory.Television);
        CandidateChannel tuned = Candidate(101, 27, selected: true);
        Held(102, ServiceCategory.Radio);
        Candidate(102, 27, selected: true);
        Held(103, ServiceCategory.Television);
        Candidate(103, 28, selected: false);

        IReadOnlyList<WatchableChannel> listed = await Service.ListAsync(Cancel);

        WatchableChannel only = Assert.Single(listed);
        Assert.Same(television, only.Service);
        Assert.Same(tuned, only.Selected);
    }

    private BroadcastService Held(int serviceId, ServiceCategory category)
    {
        BroadcastService service = BroadcastService.Discover(new NetworkId(4), new ServiceId(serviceId), "Fixture Service", category, At);

        services.Services.Add(service);

        return service;
    }

    private CandidateChannel Candidate(int serviceId, int physical, bool selected)
    {
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(serviceId),
            TuningParameters.Terrestrial(physical),
            At);

        if (selected)
        {
            candidate.Select(SelectionSource.Manual, null, At);
        }

        candidates.Candidates.Add(candidate);

        return candidate;
    }
}
