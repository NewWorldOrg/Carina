using Carina.Broadcast.Tables;
using Carina.Broadcast.Text;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWriterTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = StillClock.Now.UtcDateTime;

    private static int nextNetworkId = 50000;

    [Fact]
    public async Task ATableTurnsIntoProgrammesTheStoreCanHandBack()
    {
        var network = NextNetwork();
        await using var context = database.Open();

        var written = await Writer(context).WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(new ProgrammesWritten(1, 0, 0), written);

        await using var reading = database.Open();
        var stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        Assert.Equal("あさイチ", stored!.Name);
        Assert.Equal(new DateTime(2026, 8, 17, 13, 57, 0, DateTimeKind.Utc), stored.StartsAt);
        Assert.Equal(TimeSpan.FromMinutes(3), stored.EndsAt - stored.StartsAt);
        Assert.Equal(ProgrammeSource.PresentFollowing, stored.Source);
    }

    [Fact]
    public async Task TheSameTableArrivingAgainWritesNothingNew()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var writer = Writer(context);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(new ProgrammesWritten(0, 0, 0), await writer.WriteAsync([Table(network, 1)], Cancel));
    }

    [Fact]
    public async Task ATableThatSaysSomethingNewUpdatesWhatWasThere()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var writer = Writer(context);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(
            new ProgrammesWritten(0, 1, 0),
            await writer.WriteAsync([Table(network, 1, name: "ひるまえほっと")], Cancel));

        await using var reading = database.Open();

        Assert.Equal(
            "ひるまえほっと",
            (await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel))!.Name);
    }

    [Fact]
    public async Task EventsTheTableItselfThrewAwayAreCountedHereToo()
    {
        var network = NextNetwork();
        await using var context = database.Open();

        var written = await Writer(context).WriteAsync([Table(network, 1, unreadableStart: true)], Cancel);

        Assert.Equal(new ProgrammesWritten(0, 0, 1), written);
    }

    private static ProgrammeWriter Writer(CarinaDbContext context)
        => new(new ProgrammeRepository(context), new UnguardedWrites(), new StillClock());

    private static int NextNetwork() => Interlocked.Increment(ref nextNetworkId);

    private static ProgrammeId Id(int network, int carried)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private static EventInformationTable Table(
        int network,
        int carried,
        string name = "あさイチ",
        bool unreadableStart = false)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = 1049,
                LastSectionNumber = 1,
                Body =
                [
                    0x7F, 0xE3,
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, 0x4E,
                    .. Event(carried, name, unreadableStart),
                ],
            }))).Table;

    private static byte[] Event(int carried, string name, bool unreadableStart)
    {
        byte[] written = [.. name.SelectMany(letter => Kanji(letter))];
        byte[] descriptor = [0x4D, (byte)(5 + written.Length), 0x6A, 0x70, 0x6E, (byte)written.Length, .. written, 0x00];

        return
        [
            (byte)(carried >> 8), (byte)(carried & 0xFF),
            0xEF, 0x55, unreadableStart ? (byte)0x2A : (byte)0x22, 0x57, 0x00,
            0x00, 0x03, 0x00,
            0x00, (byte)descriptor.Length,
            .. descriptor,
        ];
    }

    private static byte[] Kanji(char letter)
    {
        for (var row = JisX0208.FirstRow; row <= JisX0208.LastRow; row++)
        {
            for (var cell = 1; cell <= JisX0208.CellsPerRow; cell++)
            {
                if (JisX0208.TryMap(row, cell, out var mapped) && mapped == letter)
                {
                    return [(byte)(row + 0x20), (byte)(cell + 0x20)];
                }
            }
        }

        throw new InvalidOperationException($"No broadcast can send '{letter}'.");
    }
}
