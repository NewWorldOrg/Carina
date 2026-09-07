using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationSourceShapeTests
{
    [Fact]
    public void APathIsReadFromTheSourceOutputDirectoryDown()
    {
        Assert.Throws<ArgumentException>(() => new SourceFile("/elsewhere/one.m2ts", 100));
        Assert.Throws<ArgumentException>(
            () => new SourceRecordingFile(7, "/elsewhere/one.m2ts", SourceFileKind.AsBroadcast, 100));
    }

    [Fact]
    public void AFileIsNotSmallerThanEmpty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFile("one.m2ts", -1));
    }

    [Fact]
    public void ARowOfTheSourceSystemIsNumberedFromOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Recording(0, InReach));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceRecordingFile(0, "one.m2ts", SourceFileKind.AsBroadcast, 100));
    }

    [Fact]
    public void ARecordingEndsAfterItStarts()
    {
        Assert.Throws<ArgumentException>(() => new SourceRecording(7, "a programme", Ended, Began, InReach));
    }

    [Fact]
    public void ATwoPartServiceIdentityIsTheSameWhicheverObjectHoldsIt()
    {
        Assert.Equal(ServiceKey.Of(32736, 1024), ServiceKey.Of(32736, 1024));
        Assert.NotEqual(ServiceKey.Of(32736, 1024), ServiceKey.Of(32736, 1025));
    }

    [Fact]
    public void ASnapshotOfTheSourceHoldsTheFivePopulationsItRead()
    {
        SourceLedger read = Ledger(
            recordings: [Recording(7)],
            files: [AsBroadcast(7, "one.m2ts", 100)],
            rules: [Rule(3)],
            reservations: [Reservation(5, fromARule: true)],
            channels: [Channel(11, SourceBroadcastKind.Terrestrial, InReach)]);

        Assert.Equal(Source, read.Name);
        Assert.Single(read.Recordings);
        Assert.Single(read.RecordingFiles);
        Assert.Single(read.Rules);
        Assert.Single(read.Reservations);
        Assert.Single(read.ChannelDefinitions);
    }
}
