using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class RootListingTests
{
    [Fact]
    public void AListingFindsTheFileItHolds()
    {
        RootListing listing = Holding(Primary, ("one.m2ts", 100));

        Assert.Equal(100, listing.Named("one.m2ts")?.SizeBytes);
    }

    [Fact]
    public void AListingFindsNothingUnderANameItDoesNotHold()
    {
        Assert.Null(Holding(Primary, ("one.m2ts", 100)).Named("two.m2ts"));
    }

    [Fact]
    public void ANameIsMatchedExactlyAndNotByCase()
    {
        Assert.Null(Holding(Primary, ("one.m2ts", 100)).Named("ONE.M2TS"));
    }

    [Fact]
    public void AWalkedRootIsReachableEvenWhenItHoldsNothing()
    {
        RootListing listing = Empty(Primary);

        Assert.True(listing.Reachable);
        Assert.Empty(listing.Files);
    }

    [Fact]
    public void ARootOutOfReachHoldsNothingAndSaysSo()
    {
        RootListing listing = RootListing.OutOfReach(Primary);

        Assert.False(listing.Reachable);
        Assert.Empty(listing.Files);
        Assert.Equal("primary", listing.Root.Value);
    }

    [Fact]
    public void OneNameListedTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Holding(Primary, ("one.m2ts", 1), ("one.m2ts", 2)));
    }

    [Fact]
    public void AListingOfNoRootIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => RootListing.Of(null!, []));
    }

    [Fact]
    public void AListingOfNoFilesAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => RootListing.Of(Primary, null!));
    }

    [Fact]
    public void AHoleInTheListingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => RootListing.Of(Primary, [null!]));
    }

    [Fact]
    public void LookingUpNothingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Empty(Primary).Named(null!));
    }

    [Fact]
    public void AListingKeepsItsOwnCopyOfWhatItWasHanded()
    {
        List<StoredFile> files = [new StoredFile("one.m2ts", 1)];
        RootListing listing = RootListing.Of(Primary, files);

        files.Clear();

        Assert.Single(listing.Files);
    }
}
