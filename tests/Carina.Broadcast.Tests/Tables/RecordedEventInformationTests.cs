using System.Reflection;

using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class RecordedEventInformationTests
{
    private static readonly TimeSpan BroadcastOffset = TimeSpan.FromHours(9);

    [Fact]
    public void ThePresentFollowingSectionNamesTheOneEventItCarries()
    {
        EventInformationTable table = Table(0);

        Assert.True(table.IsPresentFollowing);
        Assert.Equal(1049, table.ServiceId);
        Assert.Equal(32739, table.TransportStreamId);
        Assert.Equal(32739, table.OriginalNetworkId);

        DescribedEvent carried = Assert.Single(table.Events);

        Assert.Equal(47289, carried.EventId);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 22, 57, 0, BroadcastOffset), carried.StartsAt);
        Assert.Equal(TimeSpan.FromMinutes(3), carried.Runs);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 23, 0, 0, BroadcastOffset), carried.EndsAt);
    }

    [Fact]
    public void ThePresentFollowingEventReadsBackTheTitleAsItWasBroadcast()
    {
        ShortEventDescription? described = Assert.Single(Table(0).Events).Described;

        Assert.NotNull(described);
        Assert.Equal("jpn", described.Language);
        Assert.Equal("トップニュース先出し\U0001F211", described.Name);
        Assert.StartsWith("きょうのnews23。", described.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScheduleSectionCarriesEventsThatRunOneAfterAnother()
    {
        EventInformationTable table = Table(1);

        Assert.False(table.IsPresentFollowing);
        Assert.Equal(72, table.SectionNumber);
        Assert.Equal(72, table.SegmentLastSectionNumber);
        Assert.Equal(0x51, table.LastTableId);

        Assert.Equal(
            [47300, 47301, 47303],
            table.Events.Select(carried => carried.EventId));

        Assert.Equal(
            [
                new DateTimeOffset(2026, 8, 18, 3, 45, 0, BroadcastOffset),
                new DateTimeOffset(2026, 8, 18, 4, 30, 0, BroadcastOffset),
                new DateTimeOffset(2026, 8, 18, 5, 20, 0, BroadcastOffset),
            ],
            table.Events.Select(carried => carried.StartsAt));

        Assert.Equal(
            table.Events.Take(2).Select(carried => carried.EndsAt),
            table.Events.Skip(1).Select(carried => (DateTimeOffset?)carried.StartsAt));
    }

    [Fact]
    public void TheLongDescriptionIsGatheredUnderTheHeadingsItWasBroadcastWith()
    {
        ExtendedEventDescription? detailed = Assert.Single(Table(2).Events).Detailed;

        Assert.NotNull(detailed);
        Assert.Equal("jpn", detailed.Language);

        Assert.Equal(
            ["番組内容", "公式ページ", "おことわり"],
            detailed.Items.Select(item => item.Heading));

        Assert.StartsWith(
            "このあとすぐ始まる",
            detailed.Items[0].Text,
            StringComparison.Ordinal);

        Assert.Equal("番組の内容と放送時間は変更になる可能性があります。", detailed.Items[^1].Text);
    }

    [Fact]
    public void TheLongDescriptionKeepsTheLinesItWasBroadcastOn()
    {
        ExtendedEventDescription? detailed = Assert.Single(Table(2).Events).Detailed;

        Assert.NotNull(detailed);

        string[] lines = detailed.Items[1].Text.Split('\n');

        Assert.Equal(7, lines.Length);
        Assert.DoesNotContain(lines, line => line.Length == 0);
        Assert.All(detailed.Items, item => Assert.False(item.Text.EndsWith('\n')));
    }

    [Fact]
    public void TheDescriptionArrivesSpreadOverSeveralDescriptors()
    {
        int carried = Assert.Single(Table(2).Events).Descriptors
            .Count(descriptor => descriptor.Tag == DescriptorTags.ExtendedEvent);

        Assert.True(carried > 1, $"expected the recording to spread the description, saw {carried} descriptor");
        Assert.DoesNotContain(Assert.Single(Table(2).Events).Detailed!.Items, item => item.Heading.Length == 0);
    }

    [Fact]
    public void ASectionWithoutALongDescriptionSaysSoRatherThanInventingOne()
    {
        Assert.Null(Assert.Single(Table(0).Events).Detailed);
    }

    [Fact]
    public void TheRecordedEventNamesItsGenresStreamsAndTheEventsItSharesWith()
    {
        DescribedEvent carried = Assert.Single(Table(2).Events);

        Assert.Equal([0, 11], carried.Genres.Select(genre => genre.Kind));
        Assert.Equal(1, Assert.Single(carried.Components).StreamContent);
        Assert.Equal(2, Assert.Single(carried.AudioComponents).StreamContent);

        EventGrouping grouping = Assert.Single(carried.Groupings);

        Assert.Equal(EventGroupKind.Shared, grouping.Kind);

        Assert.Equal(
            [(1048, 47289), (1049, 47289)],
            grouping.Events.Select(shared => (shared.ServiceId, shared.EventId)));
    }

    [Fact]
    public void AShortListeningLeavesTheScheduleUnfinishedAndSaysWhichSegmentsAreMissing()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 0, 0, 0));

        foreach (Section section in Sections())
        {
            if (EventInformationTable.Read(section) is TableRead<EventInformationTable>.Parsed parsed)
            {
                progress.Saw(parsed.Table);
            }
        }

        ScheduledService service = Assert.Single(progress.Services);

        Assert.Equal(new ScheduledService(32739, 32739, 1049), service);
        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);

        IReadOnlyList<int> awaited = progress.SegmentsAwaited(service, EventInformationTable.FirstScheduleActualTableId);

        Assert.Equal(31, awaited.Count);
        Assert.DoesNotContain(9, awaited);
    }

    [Fact]
    public void ASectionOfADifferentTableIsRefusedRatherThanRead()
    {
        TableRead<EventInformationTable>.Rejected refused = Assert.IsType<TableRead<EventInformationTable>.Rejected>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = ServiceDescriptionTable.ActualStreamTableId,
                TableIdExtension = 1,
            })));

        Assert.Equal(TableDefect.WrongTableId, refused.Defect);
    }

    private static EventInformationTable Table(int index)
    {
        TableRead<EventInformationTable> read = EventInformationTable.Read(Sections()[index]);

        return Assert.IsType<TableRead<EventInformationTable>.Parsed>(read).Table;
    }

    private static IReadOnlyList<Section> Sections()
    {
        using Stream carried = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Carina.Broadcast.Tests.Tables.Broadcasts.eit-sections.bin")
            ?? throw new InvalidOperationException("The recorded sections are missing from the test assembly.");

        using var held = new MemoryStream();
        carried.CopyTo(held);

        byte[] bytes = held.ToArray();
        var sections = new List<Section>();
        int at = 0;

        while (at + 2 <= bytes.Length)
        {
            int length = (bytes[at] << 8) | bytes[at + 1];
            at += 2;

            var assembler = new SectionAssembler(EventInformationTable.Pid);

            sections.Add(new TransportStreamWriter(EventInformationTable.Pid)
                .Sections(bytes[at..(at + length)])
                .Packets
                .SelectMany(packet => assembler.Push(packet))
                .OfType<SectionRead.Assembled>()
                .Single()
                .Section);

            at += length;
        }

        return sections;
    }
}
