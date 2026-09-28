using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class BroadcastGroupsFromTheGuideTests
{
    private const int Network = 32002;

    private const int Stream = 32002;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTimeOffset Noon = StillClock.Now.AddHours(3);

    private static readonly DateTime At = StillClock.Now.UtcDateTime;

    [Fact]
    public async Task BrRd010AMovedBroadcastReadFromTheGuideIsReservedOnceOnTheListingThePresentFollowingTableSaysIsRunning()
    {
        HeldProgrammes held = await ReadAsync(
            Table(EventInformationTable.FirstScheduleActualTableId, 1, Event(11, Noon, 120, "Moved", Group(EventGroupKind.Moved, (1, 11), (2, 21)))),
            Table(EventInformationTable.FirstScheduleActualTableId, 2, Event(21, Noon.AddMinutes(30), 90, "Moved", Group(EventGroupKind.Moved, (1, 11), (2, 21)))),
            Table(EventInformationTable.PresentFollowingActualTableId, 2, Event(21, Noon.AddMinutes(30), 90, "Moved", running: 4)));

        var resolver = BroadcastGroupResolver.Of(await held.ListGroupedAsync(Cancel));
        BroadcastResolution fromTheEarlier = resolver.Resolve(Find(held, 1, 11), At);
        BroadcastResolution fromTheRunning = resolver.Resolve(Find(held, 2, 21), At);

        BroadcastTarget target = Assert.Single(fromTheEarlier.Targets);
        Assert.Equal(Id(2, 21), target.Programme.Id);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, target.Role);
        Assert.Equal(new BroadcastGroupKey($"movement:{Network}-1-11"), target.Key);
        Assert.Equal(BroadcastExclusion.Moved, fromTheEarlier.Exclusion);
        Assert.Equal(fromTheEarlier.Targets, fromTheRunning.Targets);
        Assert.Equal(BroadcastExclusion.None, fromTheRunning.Exclusion);
    }

    [Fact]
    public async Task BrRd010ARelayReadFromTheGuideIsReservedSegmentBySegment()
    {
        HeldProgrammes held = await ReadAsync(
            Table(EventInformationTable.FirstScheduleActualTableId, 1, Event(12, Noon, 60, "Relay", Group(EventGroupKind.Relayed, (3, 31)))),
            Table(EventInformationTable.FirstScheduleActualTableId, 3, Event(31, Noon.AddMinutes(60), 60, "Relay continued")));

        var resolver = BroadcastGroupResolver.Of(await held.ListGroupedAsync(Cancel));
        BroadcastResolution resolved = resolver.Resolve(Find(held, 3, 31), At);

        Assert.Equal([Id(1, 12), Id(3, 31)], resolved.Targets.Select(target => target.Programme.Id));
        Assert.All(resolved.Targets, target => Assert.Equal(BroadcastGroupRole.RelaySegment, target.Role));
        Assert.All(
            resolved.Targets,
            target => Assert.Equal(new BroadcastGroupKey($"relay:{Network}-1-12"), target.Key));
    }

    [Fact]
    public async Task BrRd010AShadowReadFromTheGuideStandsForNothing()
    {
        HeldProgrammes held = await ReadAsync(
            Table(EventInformationTable.FirstScheduleActualTableId, 1, Event(13, Noon, 60, "Shared")),
            Table(EventInformationTable.FirstScheduleActualTableId, 4, Event(41, Noon, 60, null, Group(EventGroupKind.Shared, (1, 13)))));

        var resolver = BroadcastGroupResolver.Of(await held.ListGroupedAsync(Cancel));
        BroadcastResolution resolved = resolver.Resolve(Find(held, 4, 41), At);

        Assert.Empty(resolved.Targets);
        Assert.Equal(BroadcastExclusion.Shadow, resolved.Exclusion);
    }

    [Fact]
    public async Task BrRd010AGroupReadsTheProgrammesItNamesEvenWhenOnlyOneSideCarriesTheLink()
    {
        HeldProgrammes held = await ReadAsync(
            Table(EventInformationTable.FirstScheduleActualTableId, 1, Event(12, Noon, 60, "Relay", Group(EventGroupKind.Relayed, (3, 31)))),
            Table(EventInformationTable.FirstScheduleActualTableId, 3, Event(31, Noon.AddMinutes(60), 60, "Relay continued")),
            Table(EventInformationTable.FirstScheduleActualTableId, 5, Event(51, Noon, 60, "Unrelated")));

        IReadOnlyList<Programme> grouped = await held.ListGroupedAsync(Cancel);

        Assert.Equal([Id(1, 12), Id(3, 31)], grouped.Select(programme => programme.Id).Order(ById.Instance));
    }

    private static async Task<HeldProgrammes> ReadAsync(params EventInformationTable[] tables)
    {
        var held = new HeldProgrammes();
        var writer = new ProgrammeWriter(held, new UnguardedWrites(), new StillClock(), new SilentEvents(), new CountedNotices());

        foreach (EventInformationTable table in tables)
        {
            await writer.WriteAsync([table], [], Cancel);
        }

        return held;
    }

    private static Programme Find(HeldProgrammes held, int service, int carried)
        => held.Programmes.Single(programme => programme.Id.Equals(Id(service, carried)));

    private static ProgrammeId Id(int service, int carried)
        => new(new NetworkId(Network), new ServiceId(service), new EventId(carried));

    private static byte[] Group(EventGroupKind kind, params (int ServiceId, int EventId)[] events)
        => SiDescriptorWriter.EventGroup(kind, events);

    private static byte[] Event(
        int carried,
        DateTimeOffset startsAt,
        int minutes,
        string? name,
        byte[]? group = null,
        int running = 0)
    {
        byte[] described = name is null
            ? []
            : SiDescriptorWriter.ShortEvent(Text(name), Text(string.Empty));

        return EitWriter.Event(
            carried,
            startsAt,
            TimeSpan.FromMinutes(minutes),
            [.. described, .. group ?? []],
            running);
    }

    private static byte[] Text(string text)
        => new AribTextWriter().DesignateAlphanumericToG0().Ascii(text).ToArray();

    private static EventInformationTable Table(int tableId, int service, params byte[][] events)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = service,
                LastSectionNumber = tableId == EventInformationTable.PresentFollowingActualTableId ? 1 : 0,
                Body = new EitWriter
                {
                    TransportStreamId = Stream,
                    OriginalNetworkId = Network,
                    LastTableId = tableId,
                    Events = events,
                }.ToBody(),
            }))).Table;

    private sealed class ById : IComparer<ProgrammeId>
    {
        public static readonly ById Instance = new();

        public int Compare(ProgrammeId? x, ProgrammeId? y)
            => string.CompareOrdinal(x?.ToString(), y?.ToString());
    }
}
