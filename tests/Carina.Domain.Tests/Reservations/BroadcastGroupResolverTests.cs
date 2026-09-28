using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class BroadcastGroupResolverTests
{
    private const int Network = 32001;

    private static readonly DateTime At = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BrRd010AProgrammeInNoGroupStandsForItself()
    {
        Programme alone = Listing(1, 101, 60, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([]);

        BroadcastResolution resolved = resolver.Resolve(alone, At);

        BroadcastTarget target = Assert.Single(resolved.Targets);
        Assert.Same(alone, target.Programme);
        Assert.Null(target.Key);
        Assert.Equal(BroadcastGroupRole.Standalone, target.Role);
        Assert.Equal(BroadcastExclusion.None, resolved.Exclusion);
    }

    [Fact]
    public void BrRd010AShadowStandsForNothing()
    {
        Programme shadow = Listing(2, 201, 60, 120, isShadow: true, related: [Link(1, 101, RelationKind.Shared)]);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([shadow]);

        BroadcastResolution resolved = resolver.Resolve(shadow, At);

        Assert.Empty(resolved.Targets);
        Assert.Equal(BroadcastExclusion.Shadow, resolved.Exclusion);
    }

    [Fact]
    public void BrRd010OverlappingListingsOfAMovedBroadcastAreReservedOnceOnTheEarliest()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme second = Listing(2, 201, 90, 150, related: [Link(1, 101, RelationKind.Moved)]);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([second, first]);

        BroadcastResolution fromFirst = resolver.Resolve(first, At);
        BroadcastResolution fromSecond = resolver.Resolve(second, At);

        BroadcastTarget target = Assert.Single(fromFirst.Targets);
        Assert.Same(first, target.Programme);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, target.Role);
        Assert.Equal(new BroadcastGroupKey($"movement:{Network}-1-101"), target.Key);
        Assert.Equal(BroadcastExclusion.None, fromFirst.Exclusion);
        Assert.Equal([target], fromSecond.Targets);
        Assert.Equal(BroadcastExclusion.Moved, fromSecond.Exclusion);
    }

    [Fact]
    public void BrRd010AListingThePresentFollowingTableSaysIsRunningIsThePrimaryWhateverStartsFirst()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme running = Listing(2, 201, 90, 150, running: ProgrammeRunning.Running);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, running]);

        BroadcastResolution resolved = resolver.Resolve(first, At);

        Assert.Same(running, Assert.Single(resolved.Targets).Programme);
        Assert.Equal(BroadcastExclusion.Moved, resolved.Exclusion);
    }

    [Fact]
    public void BrRd010ListingsStartingTogetherAreSettledByTheirIdentifiers()
    {
        Programme later = Listing(3, 301, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme earlier = Listing(2, 201, 60, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([later, earlier]);

        Assert.Same(earlier, Assert.Single(resolver.Resolve(later, At).Targets).Programme);
        Assert.Same(earlier, Assert.Single(resolver.Resolve(earlier, At).Targets).Programme);
    }

    [Fact]
    public void BrRd010ListingsOfAMovedBroadcastThatDoNotOverlapAreEachReserved()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme second = Listing(2, 201, 120, 180);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, second]);

        Assert.Same(first, Assert.Single(resolver.Resolve(first, At).Targets).Programme);
        Assert.Same(second, Assert.Single(resolver.Resolve(second, At).Targets).Programme);
        Assert.Equal(BroadcastExclusion.None, resolver.Resolve(second, At).Exclusion);
    }

    [Fact]
    public void BrRd010AShadowInAMovedBroadcastIsNeverItsPrimary()
    {
        Programme shadow = Listing(1, 101, 0, 120, isShadow: true, related: [Link(2, 201, RelationKind.Moved)]);
        Programme listed = Listing(2, 201, 30, 150);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([shadow, listed]);

        BroadcastResolution resolved = resolver.Resolve(listed, At);

        Assert.Same(listed, Assert.Single(resolved.Targets).Programme);
        Assert.Equal(BroadcastExclusion.None, resolved.Exclusion);
    }

    [Fact]
    public void BrRd010EverySegmentOfARelayedBroadcastIsReservedUnderOneKey()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme second = Listing(2, 201, 120, 180, related: [Link(3, 301, RelationKind.Relayed)]);
        Programme third = Listing(3, 301, 180, 240);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([third, first, second]);

        foreach (Programme asked in new[] { first, second, third })
        {
            BroadcastResolution resolved = resolver.Resolve(asked, At);

            Assert.Equal([first, second, third], resolved.Targets.Select(target => target.Programme));
            Assert.All(resolved.Targets, target => Assert.Equal(BroadcastGroupRole.RelaySegment, target.Role));
            Assert.All(
                resolved.Targets,
                target => Assert.Equal(new BroadcastGroupKey($"relay:{Network}-1-101"), target.Key));
            Assert.Equal(BroadcastExclusion.None, resolved.Exclusion);
        }
    }

    [Fact]
    public void BrRd010ASegmentThatHasEndedIsNotReservedAgain()
    {
        Programme first = Listing(1, 101, -120, -60, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme second = Listing(2, 201, 60, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, second]);

        Assert.Same(second, Assert.Single(resolver.Resolve(second, At).Targets).Programme);
    }

    [Fact]
    public void BrRd010ASegmentThatIsAMovedListingIsReservedOnItsPrimary()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme segment = Listing(2, 201, 120, 180, related: [Link(3, 301, RelationKind.Moved)]);
        Programme primary = Listing(3, 301, 120, 180, running: ProgrammeRunning.Running);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, segment, primary]);

        BroadcastResolution resolved = resolver.Resolve(first, At);

        Assert.Equal([first, primary], resolved.Targets.Select(target => target.Programme));
        Assert.All(resolved.Targets, target => Assert.Equal(BroadcastGroupRole.RelaySegment, target.Role));
    }

    [Fact]
    public void BrRd010AGroupNamedFromOneSideOnlyIsStillAGroup()
    {
        Programme naming = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved)]);
        Programme named = Listing(2, 201, 60, 120, running: ProgrammeRunning.Running);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([naming, named]);

        Assert.Same(named, Assert.Single(resolver.Resolve(named, At).Targets).Programme);
        Assert.Equal(BroadcastExclusion.Moved, resolver.Resolve(naming, At).Exclusion);
    }

    [Fact]
    public void BrRd010TheKeyNamesTheSmallestMemberEvenWhenThatOneIsNotInTheGuide()
    {
        Programme listed = Listing(2, 201, 60, 120, related: [Link(1, 5, RelationKind.Relayed), Link(3, 301, RelationKind.Relayed)]);
        Programme after = Listing(3, 301, 120, 180);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([listed, after]);

        BroadcastResolution resolved = resolver.Resolve(after, At);

        Assert.All(
            resolved.Targets,
            target => Assert.Equal(new BroadcastGroupKey($"relay:{Network}-1-5"), target.Key));
    }

    [Fact]
    public void BrRd010TheAnswerDoesNotDependOnTheOrderTheGuideHandsTheListingsOver()
    {
        Programme[] listings =
        [
            Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Moved), Link(4, 401, RelationKind.Relayed)]),
            Listing(2, 201, 60, 120, related: [Link(3, 301, RelationKind.Moved)]),
            Listing(3, 301, 90, 150),
            Listing(4, 401, 150, 210),
        ];

        string[] answers =
        [
            .. Enumerable.Range(0, listings.Length).Select(turn =>
            {
                BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([.. listings.Skip(turn), .. listings.Take(turn)]);

                return string.Join(
                    ";",
                    listings.Select(asked => string.Join(
                        ",",
                        resolver.Resolve(asked, At).Targets.Select(target =>
                            $"{target.Programme.Id}/{target.Key?.Value}/{target.Role}"))));
            }),
        ];

        Assert.Single(answers.Distinct());
    }

    [Fact]
    public void BrRd010AReservationOnASuppressedListingIsPlacedOnThePrimary()
    {
        Programme primary = Listing(1, 101, 60, 120, running: ProgrammeRunning.Running, related: [Link(2, 201, RelationKind.Moved)]);
        Programme suppressed = Listing(2, 201, 50, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([primary, suppressed]);

        BroadcastPlacement? placed = resolver.Place(Reference(suppressed), suppressed.EndsAt!.Value, At);

        Assert.NotNull(placed);
        Assert.Same(primary, placed.Own.Programme);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, placed.Own.Role);
        Assert.Empty(placed.Alongside);
    }

    [Fact]
    public void BrRd010AReservationWhoseListingLeftTheGuideIsPlacedOnThePrimaryOfTheListingsOverlappingIt()
    {
        Programme remaining = Listing(2, 201, 60, 120, related: [Link(1, 101, RelationKind.Moved)]);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([remaining]);
        ProgrammeRef gone = Reference(1, 101, 60);

        BroadcastPlacement? placed = resolver.Place(gone, At.AddMinutes(120), At);

        Assert.Same(remaining, placed!.Own.Programme);
    }

    [Fact]
    public void BrRd010AReservationWhoseListingLeftTheGuideWithNothingOverlappingItIsNotPlaced()
    {
        Programme remaining = Listing(2, 201, 300, 360, related: [Link(1, 101, RelationKind.Moved)]);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([remaining]);

        Assert.Null(resolver.Place(Reference(1, 101, 60), At.AddMinutes(120), At));
    }

    [Fact]
    public void BrRd010AReservationOnARelaySegmentIsPlacedWithTheSegmentsStillToCome()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme second = Listing(2, 201, 180, 240);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, second]);

        BroadcastPlacement? placed = resolver.Place(Reference(first), first.EndsAt!.Value, At);

        Assert.Same(first, placed!.Own.Programme);
        Assert.Equal(BroadcastGroupRole.RelaySegment, placed.Own.Role);
        Assert.Same(second, Assert.Single(placed.Alongside).Programme);
    }

    [Fact]
    public void BrRd010AReservationOnAListingInNoGroupIsPlacedAsStandingAlone()
    {
        Programme alone = Listing(1, 101, 60, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([alone]);

        BroadcastPlacement? placed = resolver.Place(Reference(alone), alone.EndsAt!.Value, At);

        Assert.Equal(BroadcastGroupRole.Standalone, placed!.Own.Role);
        Assert.Null(placed.Own.Key);
    }

    [Fact]
    public void BrRd010TheMembersAlongsideAProgrammeAreEveryListingOfItsGroupsInTheGuide()
    {
        Programme first = Listing(1, 101, 60, 120, related: [Link(2, 201, RelationKind.Relayed)]);
        Programme second = Listing(2, 201, 120, 180, related: [Link(3, 301, RelationKind.Moved)]);
        Programme moved = Listing(3, 301, 120, 180);
        Programme elsewhere = Listing(4, 401, 60, 120);
        BroadcastGroupResolver resolver = BroadcastGroupResolver.Of([first, second, moved, elsewhere]);

        Assert.Equal([first, second], resolver.MembersAlongside([first.Id]));
        Assert.Equal([first, second, moved], resolver.MembersAlongside([second.Id]));
        Assert.Empty(resolver.MembersAlongside([elsewhere.Id]));
    }

    private static RelatedProgramme Link(int service, int carried, RelationKind kind)
        => new(Network, service, carried, kind);

    private static ProgrammeRef Reference(Programme programme)
        => new(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt);

    private static ProgrammeRef Reference(int service, int carried, int startMinutes)
        => new(new NetworkId(Network), new ServiceId(service), new EventId(carried), At.AddMinutes(startMinutes));

    private static Programme Listing(
        int service,
        int carried,
        int startMinutes,
        int endMinutes,
        bool isShadow = false,
        ProgrammeRunning running = ProgrammeRunning.Undetermined,
        IReadOnlyList<RelatedProgramme>? related = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(32001),
            At.AddMinutes(startMinutes),
            At.AddMinutes(endMinutes),
            isShadow ? string.Empty : $"番組 {service}-{carried}",
            string.Empty,
            isShadow,
            At,
            related: related,
            running: running);
}
