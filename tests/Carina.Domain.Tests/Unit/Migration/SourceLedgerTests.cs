using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class SourceLedgerTests
{
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
