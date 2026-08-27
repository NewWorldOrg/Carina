using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class StorageStandingTests
{
    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Bulk = new("bulk");

    [Fact]
    public void ARootTheDriverDeclaredIsAnsweredWithWhatTheDriverSaidAboutIt()
    {
        StorageRootStanding standing = Assert.Single(
            StorageStanding.Of([Root("primary", free: 900, total: 1_000, writable: true)], [], Noon));

        Assert.Equal("primary", standing.Name);
        Assert.Equal(900, standing.FreeBytes);
        Assert.Equal(1_000, standing.TotalBytes);
        Assert.True(standing.Writable);
    }

    [Fact]
    public void ARootWithNothingRunningOnItHasNothingSpokenForAndNoShortfall()
    {
        StorageRootStanding standing = Assert.Single(
            StorageStanding.Of([Root("primary", free: 900, total: 1_000, writable: true)], [], Noon));

        Assert.Equal(Int128.Zero, standing.CommittedBytes);
        Assert.Equal(0, standing.RecordingsInFlight);
        Assert.Null(standing.Shortfall);
    }

    [Fact]
    public void WhatIsStillToBeWrittenOnARootIsCountedAgainstThatRootAlone()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true),
             Root("bulk", free: long.MaxValue, total: long.MaxValue, writable: true)],
            [Running(Primary, TimeSpan.FromHours(1)), Running(Primary, TimeSpan.FromHours(1))],
            Noon);

        Assert.Equal(2, standing[0].RecordingsInFlight);
        Assert.Equal(0, standing[1].RecordingsInFlight);
        Assert.Equal((Int128)14_850_000_000, standing[0].CommittedBytes);
        Assert.Equal(Int128.Zero, standing[1].CommittedBytes);
    }

    [Fact]
    public void OnlyWhatIsLeftOfAWindowIsSpokenFor()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true)],
            [new RootDemand(
                Primary,
                RecordingDemand.AtTheHeaviestRate(Noon.AddMinutes(-30), Noon.AddMinutes(30)))],
            Noon);

        Assert.Equal((Int128)3_712_500_000, standing[0].CommittedBytes);
    }

    [Fact]
    public void ARootWhoseFreeRoomIsBelowWhatIsSpokenForSaysSo()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("primary", free: 1_000, total: 100_000_000_000, writable: true)],
            [Running(Primary, TimeSpan.FromHours(1))],
            Noon);

        Assert.Equal(DiskShortfall.ShortOfTheEstimate, standing[0].Shortfall);
    }

    [Fact]
    public void ARootThatCouldNotBeMeasuredIsToldApartFromOneWithNoRoomLeft()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("unreachable", free: 0, total: 0, writable: false),
             Root("full", free: 0, total: 1_000, writable: true)],
            [],
            Noon);

        Assert.Equal(DiskShortfall.RootUnmeasured, standing[0].Shortfall);
        Assert.Equal(DiskShortfall.NoRoomLeft, standing[1].Shortfall);
    }

    [Fact]
    public void ARootTheDriverWillNotWriteToSaysSoRatherThanLookingHealthy()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("primary", free: 900, total: 1_000, writable: false)],
            [],
            Noon);

        Assert.Equal(DiskShortfall.RootNotWritable, standing[0].Shortfall);
    }

    [Fact]
    public void ARootOnlyTheLedgerKnowsIsListedAsOneTheDriverNeverDeclared()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true)],
            [Running(Bulk, TimeSpan.FromHours(1))],
            Noon);

        Assert.Equal(["primary", "bulk"], standing.Select(root => root.Name).ToArray());
        Assert.Equal(DiskShortfall.RootUndeclared, standing[1].Shortfall);
        Assert.Equal(1, standing[1].RecordingsInFlight);
        Assert.Equal((Int128)7_425_000_000, standing[1].CommittedBytes);
        Assert.Equal(0, standing[1].FreeBytes);
        Assert.Equal(0, standing[1].TotalBytes);
        Assert.False(standing[1].Writable);
    }

    [Fact]
    public void ARootOnlyTheLedgerKnowsIsListedOnceHoweverManyRecordingsNameIt()
    {
        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [],
            [Running(Bulk, TimeSpan.FromHours(1)), Running(Bulk, TimeSpan.FromHours(1))],
            Noon);

        Assert.Equal(["bulk"], standing.Select(root => root.Name).ToArray());
        Assert.Equal(2, standing[0].RecordingsInFlight);
    }

    [Fact]
    public void ANameTheDriverDeclaredThatThisSystemWouldNotHaveChosenIsStillAnswered()
    {
        Assert.Throws<ArgumentException>(() => new OutputRoot("spare disk"));

        IReadOnlyList<StorageRootStanding> standing = StorageStanding.Of(
            [Root("spare disk", free: 900, total: 1_000, writable: true)],
            [],
            Noon);

        Assert.Equal(["spare disk"], standing.Select(root => root.Name).ToArray());
        Assert.Equal(900, standing[0].FreeBytes);
    }

    [Fact]
    public void TheSameNameDeclaredTwiceIsRefusedRatherThanAnsweredTwice()
    {
        Assert.Throws<ArgumentException>(() => StorageStanding.Of(
            [Root("primary", free: 1, total: 2, writable: true),
             Root("primary", free: 3, total: 4, writable: true)],
            [],
            Noon));
    }

    [Fact]
    public void NoRootIsAnsweredWhenTheDriverDeclaresNoneAndNothingIsRunning()
    {
        Assert.Empty(StorageStanding.Of([], [], Noon));
    }

    private static RootDemand Running(OutputRoot root, TimeSpan left)
        => new(root, RecordingDemand.AtTheHeaviestRate(Noon, Noon + left));

    private static StorageRootDto Root(string name, long free, long total, bool writable)
        => new() { Name = name, FreeBytes = free, TotalBytes = total, Writable = writable };
}
