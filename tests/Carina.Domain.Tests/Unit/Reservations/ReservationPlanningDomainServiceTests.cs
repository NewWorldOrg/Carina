using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class ReservationPlanningDomainServiceTests
{
    private const int Network = 32001;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly HeldProgrammes programmes = new();

    private readonly HeldReservationLedger reservations = new();

    private ReservationPlanningDomainService Planning
        => new(programmes, reservations, new FixedTimeProvider(new DateTimeOffset(At)));

    [Fact(DisplayName = "BR-RV-001: a programme is reserved as the guide holds it, at the start it was asked for")]
    public async Task AProgrammeIsReservedAsTheGuideHoldsIt()
    {
        Programme held = Listing(1, 101, 60, 120);

        ReservationPlanningConsidered considered = await Planning.ConsiderAsync(held.Id, held.StartsAt, Cancel);

        Assert.Null(considered.Refusal);
        Assert.Same(held, considered.Programme);
        Assert.Same(held, Assert.Single(considered.Resolution!.Targets).Programme);
        Assert.Equal(At, considered.At);
    }

    [Fact(DisplayName = "BR-RV-001: a programme the guide does not hold, or holds at another start, is not reserved")]
    public async Task AProgrammeTheGuideDoesNotHoldIsNotReserved()
    {
        Programme held = Listing(1, 101, 60, 120);

        Assert.Equal(
            ReservationPlanningRefusal.NoSuchProgramme,
            (await Planning.ConsiderAsync(held.Id, held.StartsAt.AddMinutes(1), Cancel)).Refusal);
        Assert.Equal(
            ReservationPlanningRefusal.NoSuchProgramme,
            (await Planning.ConsiderAsync(
                new ProgrammeId(new NetworkId(Network), new ServiceId(9), new EventId(9)),
                held.StartsAt,
                Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-RV-001: a shadow of another service's broadcast is not reserved")]
    public async Task AShadowIsNotReserved()
    {
        Programme shadow = Listing(2, 201, 60, 120, isShadow: true);

        Assert.Equal(
            ReservationPlanningRefusal.Shadow,
            (await Planning.ConsiderAsync(shadow.Id, shadow.StartsAt, Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-RV-001: a listing a moved broadcast suppresses is not reserved, and the refusal names the primary")]
    public async Task AListingAMovedBroadcastSuppressesIsNotReserved()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme second = Listing(2, 201, 90, 150, related: [Link(1, 101, RelationKind.Moved)]);

        ReservationPlanningConsidered considered = await Planning.ConsiderAsync(second.Id, second.StartsAt, Cancel);

        Assert.Equal(ReservationPlanningRefusal.MovedDuplicate, considered.Refusal);
        Assert.Same(first, considered.Primary);
        Assert.Same(first, await Planning.PrimaryOfMovedAsync(second.Id, Cancel));
        Assert.Null(await Planning.PrimaryOfMovedAsync(first.Id, Cancel));
    }

    [Fact(DisplayName = "BR-RV-001: a broadcast already reserved is not reserved again, and the refusal names the reservation")]
    public async Task ABroadcastAlreadyReservedIsNotReservedAgain()
    {
        Programme held = Listing(1, 101, 60, 120);
        Reservation already = Standing(held);

        ReservationPlanningConsidered considered = await Planning.ConsiderAsync(held.Id, held.StartsAt, Cancel);

        Assert.Equal(ReservationPlanningRefusal.AlreadyReserved, considered.Refusal);
        Assert.Same(already, considered.Already);
    }

    [Fact(DisplayName = "BR-RD-010: the other segments of a relayed broadcast that nothing reserves yet are reserved beside the one asked for")]
    public async Task TheOtherSegmentsNothingReservesAreReservedBeside()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme second = Listing(2, 201, 120, 180, related: [Link(3, 301, RelationKind.Relayed)]);
        Programme third = Listing(3, 301, 180, 240);
        Standing(third);
        ReservationPlanningConsidered considered = await Planning.ConsiderAsync(first.Id, first.StartsAt, Cancel);
        BroadcastTarget[] targets = [.. considered.Resolution!.Targets];

        Assert.Equal([first, second, third], targets.Select(target => target.Programme));
        Assert.False(await Planning.NeedsReservingAlongsideAsync(targets[0], first.Id, Cancel));
        Assert.True(await Planning.NeedsReservingAlongsideAsync(targets[1], first.Id, Cancel));
        Assert.False(await Planning.NeedsReservingAlongsideAsync(targets[2], first.Id, Cancel));
    }

    private static RelatedProgramme Link(int service, int carried, RelationKind kind)
        => new(Network, service, carried, kind);

    private Reservation Standing(Programme programme)
    {
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            null,
            Priority.Default,
            programme.StartsAt,
            programme.EndsAt!.Value,
            true,
            Margin.None,
            Margin.None,
            ProgrammeSnapshot.Of(programme, At),
            null,
            BroadcastGroupRole.Standalone,
            At);
        reservations.Reservations.Add(reservation);

        return reservation;
    }

    private Programme Listing(
        int service,
        int carried,
        int startMinutes,
        int endMinutes,
        bool isShadow = false,
        IReadOnlyList<RelatedProgramme>? related = null)
    {
        Programme programme = Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Network),
            At.AddMinutes(startMinutes),
            At.AddMinutes(endMinutes),
            isShadow ? string.Empty : $"番組 {service}-{carried}",
            string.Empty,
            isShadow,
            At,
            related: related);
        programmes.Programmes.Add(programme);

        return programme;
    }
}
