using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeArchivingDomainServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldProgrammes guide = new();

    private readonly HeldArchive archive = new();

    private ProgrammeArchivingDomainService Archiving => new(guide, archive);

    [Fact(DisplayName = "BR-ED-009: a programme leaves the guide only once it has been over for longer than the guide keeps it")]
    public async Task AProgrammeLeavesTheGuideOnlyOnceItHasBeenOverForLongerThanTheGuideKeepsIt()
    {
        var settings = new CollectionSettings { KeepEndedProgrammes = TimeSpan.FromHours(24) };

        guide.Programmes.Add(Ended(1, endsAt: Now.AddHours(-24).AddTicks(-1)));
        guide.Programmes.Add(Ended(2, endsAt: Now.AddHours(-24)));

        IReadOnlyList<EndedProgramme> leaving = await Archiving.ListLeavingAsync(settings, Now, Cancel);

        Assert.Equal([1], leaving.Select(ended => ended.Programme.Id.EventId.Value));
    }

    [Fact(DisplayName = "BR-ED-009: no more than five thousand programmes leave the guide in one run")]
    public async Task NoMoreThanFiveThousandLeaveTheGuideInOneRun()
    {
        for (int carried = 1; carried <= ProgrammeArchivingDomainService.MostPerRun + 1; carried++)
        {
            guide.Programmes.Add(Ended(carried, endsAt: Now.AddDays(-3).AddSeconds(carried)));
        }

        IReadOnlyList<EndedProgramme> leaving = await Archiving.ListLeavingAsync(new CollectionSettings(), Now, Cancel);

        Assert.Equal(5_000, ProgrammeArchivingDomainService.MostPerRun);
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, leaving.Count);
    }

    [Fact(DisplayName = "BR-ED-009: every programme leaving is taken out of the guide, and a shadow is not archived")]
    public async Task EveryProgrammeLeavingTheGuideIsTakenOutOfItButAShadowIsNotArchived()
    {
        guide.Programmes.Add(Ended(1, endsAt: Now.AddDays(-3)));
        guide.Programmes.Add(Ended(2, endsAt: Now.AddDays(-3), isShadow: true));
        guide.Programmes.Add(Ended(3, endsAt: Now.AddHours(-1)));

        IReadOnlyList<EndedProgramme> leaving = await Archiving.ListLeavingAsync(new CollectionSettings(), Now, Cancel);
        ProgrammesArchived archived = await Archiving.ArchiveAsync(leaving, Now, Cancel);

        Assert.Equal(new ProgrammesArchived(1, 2), archived);
        Assert.Equal([1], archive.Programmes.Select(programme => programme.EventId.Value));
        Assert.Equal(Now, Assert.Single(archive.Programmes).ArchivedAt);
        Assert.Equal([3], guide.Programmes.Select(programme => programme.Id.EventId.Value));
    }

    [Fact(DisplayName = "BR-ED-009: what the archive refuses stays in the guide")]
    public async Task WhatTheArchiveRefusesStaysInTheGuide()
    {
        guide.Programmes.Add(Ended(1, endsAt: Now.AddDays(-3)));

        var refusing = new ProgrammeArchivingDomainService(guide, new RefusingArchive());
        IReadOnlyList<EndedProgramme> leaving = await refusing.ListLeavingAsync(new CollectionSettings(), Now, Cancel);

        await Assert.ThrowsAsync<InvalidOperationException>(() => refusing.ArchiveAsync(leaving, Now, Cancel));

        Assert.Single(guide.Programmes);
    }

    [Fact(DisplayName = "BR-ED-009: with no retention set the archive lets go of nothing")]
    public async Task WithNoRetentionTheArchiveLetsGoOfNothing()
    {
        archive.Programmes.Add(Archived(1, endsAt: Now.AddYears(-5)));

        int forgotten = await Archiving.ForgetExpiredAsync(new CollectionSettings(), Now, Cancel);

        Assert.Equal(0, forgotten);
        Assert.Single(archive.Programmes);
    }

    [Fact(DisplayName = "BR-ED-009: with a retention set the archive lets go of what ended before it")]
    public async Task WithARetentionTheArchiveLetsGoOfWhatEndedBeforeIt()
    {
        var settings = new CollectionSettings { ArchiveRetention = TimeSpan.FromDays(30) };

        archive.Programmes.Add(Archived(1, endsAt: Now.AddDays(-30).AddTicks(-1)));
        archive.Programmes.Add(Archived(2, endsAt: Now.AddDays(-30)));

        int forgotten = await Archiving.ForgetExpiredAsync(settings, Now, Cancel);

        Assert.Equal(1, forgotten);
        Assert.Equal([2], archive.Programmes.Select(programme => programme.EventId.Value));
    }

    private static Programme Ended(int carried, DateTime endsAt, bool isShadow = false)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(32_736), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                endsAt.AddMinutes(-30),
                endsAt,
                isShadow ? string.Empty : "ニュース",
                string.Empty,
                isShadow),
            endsAt.AddMinutes(-30));

    private static ArchivedProgramme Archived(int carried, DateTime endsAt)
        => ArchivedProgramme.Of(new EndedProgramme(Ended(carried, endsAt), endsAt), endsAt)!;

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
