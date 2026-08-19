using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests;

public sealed class SyntheticGuideTests
{
    private const int SomeNetworkId = 50_001;
    private const int SomeTransportStreamId = 50_002;
    private const int FirstServiceId = 50_101;

    private static readonly DateTimeOffset Airs = new(2026, 8, 19, 21, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void AGeneratedProgrammeReadsBackWithItsNameAndTimesIntact()
    {
        SyntheticGuide guide = Guide(
            new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Evening Bulletin" });

        DescribedEvent carried = Assert.Single(BasicTable(guide).Events);

        Assert.Equal(1, carried.EventId);
        Assert.Equal(Airs, carried.StartsAt);
        Assert.Equal(Airs.AddMinutes(30), carried.EndsAt);
        Assert.Equal("Evening Bulletin", carried.Described?.Name);
    }

    [Fact]
    public void AProgrammeWithoutADeclaredEndReadsBackAsOpenEnded()
    {
        SyntheticGuide guide = Guide(
            new SyntheticProgramme(1, Airs, null) { Name = "Rolling Coverage" });

        DescribedEvent carried = Assert.Single(BasicTable(guide).Events);

        Assert.Null(carried.Runs);
        Assert.Null(carried.EndsAt);
    }

    [Fact]
    public void ARelayCarriesTheEventItHandsOverTo()
    {
        SyntheticGuide guide = Guide(
            new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30))
            {
                Name = "First Half",
                RelaysTo = [(FirstServiceId + 1, 9)],
            });

        DescribedEvent carried = Assert.Single(BasicTable(guide).Events);
        EventGrouping grouping = Assert.Single(carried.Groupings);

        Assert.Equal(EventGroupKind.Relayed, grouping.Kind);
        Assert.Contains(grouping.Events, held => held.ServiceId == FirstServiceId + 1 && held.EventId == 9);
    }

    [Fact]
    public void AShadowProgrammeCarriesNoNameAndASharedGroup()
    {
        SyntheticGuide guide = Guide(
            new SyntheticProgramme(2, Airs, TimeSpan.FromMinutes(30))
            {
                SharedWith = [(FirstServiceId + 1, 2)],
            });

        DescribedEvent carried = Assert.Single(BasicTable(guide).Events);
        EventGrouping grouping = Assert.Single(carried.Groupings);

        Assert.Null(carried.Described);
        Assert.Equal(EventGroupKind.Shared, grouping.Kind);
    }

    [Fact]
    public void ACorruptSectionIsRejectedWhileTheRestReadOn()
    {
        var whole = new SyntheticGuide
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = SomeTransportStreamId,
            Services = [Service(new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Survivor" })],
            CorruptSections = 2,
        };

        (EventInformationTable[] tables, _, int rejected) = ReadAll(whole);

        Assert.Equal(2, rejected);
        Assert.Contains(tables, table => table.Events.Any(held => held.EventId == 1));
    }

    [Fact]
    public void AWholeGuideReachesCompleteAndABasicOnlyGuideStopsShort()
    {
        SyntheticGuide whole = Guide(new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Whole" });
        var basicOnly = new SyntheticGuide
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = SomeTransportStreamId,
            Services = [Service(new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Basic" })],
            WithExtended = false,
        };

        Assert.Equal(ScheduleCompleteness.Complete, CompletenessOf(whole));
        Assert.Equal(ScheduleCompleteness.BasicOnly, CompletenessOf(basicOnly));
    }

    [Fact]
    public void AServiceMissingASegmentLeavesTheGuideIncomplete()
    {
        var guide = new SyntheticGuide
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = SomeTransportStreamId,
            Services =
            [
                Service(new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Short" })
                    with
                    { MissingSegment = true },
            ],
        };

        Assert.Equal(ScheduleCompleteness.Incomplete, CompletenessOf(guide));
    }

    [Fact]
    public void TheDescriptionNamesEveryServiceWithItsKind()
    {
        var guide = new SyntheticGuide
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = SomeTransportStreamId,
            Services =
            [
                new SyntheticGuideService(FirstServiceId, "Synthetic One"),
                new SyntheticGuideService(FirstServiceId + 1, "Synthetic Data") { Kind = ServiceKind.Data },
            ],
        };

        (_, ServiceDescriptionTable[] descriptions, _) = ReadAll(guide);
        ServiceDescriptionTable described = Assert.Single(descriptions);

        Assert.Equal(SomeNetworkId, described.OriginalNetworkId);
        Assert.Equal(
            [ServiceKind.Television, ServiceKind.Data],
            described.Services.Select(service => service.Kind));
        Assert.Equal("Synthetic Data", described.Services[1].Name);
    }

    private static SyntheticGuide Guide(params SyntheticProgramme[] programmes)
        => new()
        {
            NetworkId = SomeNetworkId,
            TransportStreamId = SomeTransportStreamId,
            Services = [Service(programmes)],
        };

    private static SyntheticGuideService Service(params SyntheticProgramme[] programmes)
        => new(FirstServiceId, "Synthetic One") { Programmes = programmes };

    private static EventInformationTable BasicTable(SyntheticGuide guide)
    {
        (EventInformationTable[] tables, _, _) = ReadAll(guide);

        return tables.Single(table =>
            table.TableId == EventInformationTable.FirstScheduleActualTableId
            && table.ServiceId == FirstServiceId);
    }

    private static ScheduleCompleteness CompletenessOf(SyntheticGuide guide)
    {
        var progress = new ScheduleProgress();

        foreach (EventInformationTable table in ReadAll(guide).Tables)
        {
            progress.Saw(table);
        }

        return progress.Completeness;
    }

    private static (EventInformationTable[] Tables, ServiceDescriptionTable[] Described, int Rejected) ReadAll(
        SyntheticGuide guide)
    {
        var reader = new SectionReader(EventInformationTable.Pid, ServiceDescriptionTable.Pid);
        var tables = new List<EventInformationTable>();
        var descriptions = new List<ServiceDescriptionTable>();
        int rejected = 0;

        foreach (SectionRead read in reader.Push(guide.ToBytes()))
        {
            if (read is not SectionRead.Assembled assembled)
            {
                rejected++;

                continue;
            }

            if (assembled.Pid == ServiceDescriptionTable.Pid)
            {
                if (ServiceDescriptionTable.Read(assembled.Section)
                    is TableRead<ServiceDescriptionTable>.Parsed described)
                {
                    descriptions.Add(described.Table);
                }

                continue;
            }

            if (EventInformationTable.Read(assembled.Section) is TableRead<EventInformationTable>.Parsed parsed)
            {
                tables.Add(parsed.Table);
            }
        }

        return ([.. tables], [.. descriptions], rejected);
    }
}
