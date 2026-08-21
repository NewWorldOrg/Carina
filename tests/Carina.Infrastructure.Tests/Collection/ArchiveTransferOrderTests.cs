using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class ArchiveTransferOrderTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task NothingLeavesTheGuideWhenTheArchiveRefusesToTakeIt()
    {
        var programmes = new HeldProgrammes();
        var archive = new RefusingArchive();

        programmes.Programmes.Add(Ended(Now.AddDays(-3)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Transfer(programmes, archive).RunAsync(Cancel));

        Assert.Single(programmes.Programmes);
    }

    [Fact]
    public async Task WhatTheArchiveWouldNotTakeIsNotAmongWhatTheGuideLetsGo()
    {
        var programmes = new HeldProgrammes();
        var archive = new HeldArchive();

        programmes.Programmes.Add(Ended(Now.AddDays(-3)));
        programmes.Programmes.Add(Ended(Now.AddDays(-3), carried: 2, isShadow: true));

        Transferred moved = await Transfer(programmes, archive).RunAsync(Cancel);

        Assert.Equal(1, moved.Kept);
        Assert.Equal(2, moved.Discarded);
        Assert.Empty(programmes.Programmes);
    }

    private static Programme Ended(DateTime startsAt, int carried = 1, bool isShadow = false)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(32_736), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                startsAt,
                startsAt.AddMinutes(30),
                "ニュース",
                string.Empty,
                isShadow),
            startsAt);

    private static ArchiveTransfer Transfer(HeldProgrammes programmes, IArchivedProgrammeRepository archive)
        => new(
            programmes,
            archive,
            new UnguardedWrites(),
            new CollectionSettings(),
            new FixedClock(Now),
            NullLogger<ArchiveTransfer>.Instance);

    private sealed class RefusingArchive : IArchivedProgrammeRepository
    {
        public Task<IReadOnlyList<ArchivedProgramme>> ListAsync(
            IReadOnlyList<ProgrammeService> services,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ArchivedProgramme>>([]);

        public Task<int> KeepAsync(IReadOnlyList<ArchivedProgramme> programmes, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the archive would not take them");

        public Task<int> ForgetBeforeAsync(DateTime at, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> ForgetServiceAsync(
            NetworkId networkId,
            ServiceId serviceId,
            CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
