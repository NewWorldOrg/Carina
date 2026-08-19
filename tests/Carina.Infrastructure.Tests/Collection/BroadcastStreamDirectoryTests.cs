using Carina.Domain.Channels;
using Carina.Infrastructure.Collection;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class BroadcastStreamDirectoryTests
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ServicesSharingAStreamAreOfferedAsOneVisit()
    {
        var held = new HeldCandidates();

        await held.AddAsync(Selected(4, 101, 22, 32_736), Cancel);
        await held.AddAsync(Selected(4, 102, 22, 32_736), Cancel);

        IReadOnlyList<BroadcastStream> streams = await new BroadcastStreamDirectory(held).ListAsync(Cancel);

        BroadcastStream only = Assert.Single(streams);

        Assert.Equal(new TransportStreamId(32_736), only.TransportStreamId);
        Assert.Equal([101, 102], only.Services.Select(service => service.Value));
    }

    [Fact]
    public async Task StreamsAreSeparateWhenTheirIdsDiffer()
    {
        var held = new HeldCandidates();

        await held.AddAsync(Selected(4, 101, 22, 32_736), Cancel);
        await held.AddAsync(Selected(4, 201, 24, 32_737), Cancel);

        IReadOnlyList<BroadcastStream> streams = await new BroadcastStreamDirectory(held).ListAsync(Cancel);

        Assert.Equal([32_736, 32_737], streams.Select(stream => stream.TransportStreamId.Value));
    }

    [Fact]
    public async Task AServiceWhoseStreamIsUnknownIsNotOfferedBecauseTheLedgerCouldNotKeyIt()
    {
        var held = new HeldCandidates();

        await held.AddAsync(Selected(4, 101, 22, null), Cancel);

        Assert.Empty(await new BroadcastStreamDirectory(held).ListAsync(Cancel));
    }

    [Fact]
    public async Task UnselectedCandidatesAreNotTunedTo()
    {
        var held = new HeldCandidates();
        CandidateChannel spare = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(23),
            At);

        spare.CarriedBy(new TransportStreamId(32_736));

        await held.AddAsync(spare, Cancel);

        Assert.Empty(await new BroadcastStreamDirectory(held).ListAsync(Cancel));
    }

    [Fact]
    public async Task AStreamWhoseEveryCandidateNeedsAttentionIsLeftOutOfTheWalk()
    {
        var held = new HeldCandidates();
        CandidateChannel troubled = Selected(4, 101, 22, 32_736);

        while (troubled.IsInRotation)
        {
            troubled.RecordTuningFailure(RotationBackoff.Default, At);
        }

        await held.AddAsync(troubled, Cancel);

        Assert.Empty(await new BroadcastStreamDirectory(held).ListAsync(Cancel));
    }

    [Fact]
    public async Task AStreamStillReachableThroughOneServiceCarriesAllOfThemForCoverage()
    {
        var held = new HeldCandidates();
        CandidateChannel troubled = Selected(4, 101, 22, 32_736);

        while (troubled.IsInRotation)
        {
            troubled.RecordTuningFailure(RotationBackoff.Default, At);
        }

        await held.AddAsync(troubled, Cancel);
        await held.AddAsync(Selected(4, 102, 23, 32_736), Cancel);

        BroadcastStream only = Assert.Single(await new BroadcastStreamDirectory(held).ListAsync(Cancel));

        Assert.Equal(23, only.Tuning.PhysicalChannel);
        Assert.Equal([101, 102], only.Services.Select(service => service.Value));
    }

    [Fact]
    public async Task AServiceWhoseStreamIsUnknownIsStillIntendedSoTheGapIsVisible()
    {
        var held = new HeldCandidates();
        CandidateChannel unreached = Selected(4, 101, 22, null);

        unreached.RecordTuningFailure(RotationBackoff.Default, At);

        await held.AddAsync(unreached, Cancel);

        IntendedStream only = Assert.Single(await new BroadcastStreamDirectory(held).ListIntendedAsync(Cancel));

        Assert.Null(only.TransportStreamId);
        Assert.Equal(RotationState.BackingOff, only.Reach.State);
        Assert.Equal(1, only.Reach.ConsecutiveFailures);
        Assert.NotNull(only.Reach.NextAttemptAt);
        Assert.Equal([101], only.Services.Select(service => service.Value));
    }

    [Fact]
    public async Task UnknownStreamsOnOneNetworkStayApartWhenTheyAreTunedDifferently()
    {
        var held = new HeldCandidates();

        await held.AddAsync(Selected(4, 101, 22, null), Cancel);
        await held.AddAsync(Selected(4, 102, 24, null), Cancel);

        IReadOnlyList<IntendedStream> intended = await new BroadcastStreamDirectory(held).ListIntendedAsync(Cancel);

        Assert.Equal([22, 24], intended.Select(stream => stream.Tuning.PhysicalChannel));
    }

    [Fact]
    public async Task AnIntendedStreamCarriesTheReachOfTheCandidateAWalkWouldUse()
    {
        var held = new HeldCandidates();
        CandidateChannel troubled = Selected(4, 101, 22, 32_736);

        troubled.RecordTuningFailure(RotationBackoff.Default, At);

        await held.AddAsync(troubled, Cancel);
        await held.AddAsync(Selected(4, 102, 23, 32_736), Cancel);

        IntendedStream only = Assert.Single(await new BroadcastStreamDirectory(held).ListIntendedAsync(Cancel));

        Assert.Equal(RotationState.Active, only.Reach.State);
        Assert.Equal(new TransportStreamId(32_736), only.TransportStreamId);
    }

    [Fact]
    public async Task AStreamNoCandidateCanReachIsStillIntendedThoughNoWalkOffersIt()
    {
        var held = new HeldCandidates();
        CandidateChannel troubled = Selected(4, 101, 22, 32_736);

        while (troubled.IsInRotation)
        {
            troubled.RecordTuningFailure(RotationBackoff.Default, At);
        }

        await held.AddAsync(troubled, Cancel);

        var directory = new BroadcastStreamDirectory(held);

        Assert.Empty(await directory.ListAsync(Cancel));

        IntendedStream only = Assert.Single(await directory.ListIntendedAsync(Cancel));

        Assert.Equal(RotationState.NeedsAttention, only.Reach.State);
        Assert.NotNull(only.Reach.NeedsAttentionSince);
    }

    private static CandidateChannel Selected(int network, int service, int channel, int? streamId)
    {
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(network),
            new ServiceId(service),
            TuningParameters.Terrestrial(channel),
            At);

        candidate.Select(SelectionSource.AutoSwitch, null, At);

        if (streamId is { } observed)
        {
            candidate.CarriedBy(new TransportStreamId(observed));
        }

        return candidate;
    }
}
