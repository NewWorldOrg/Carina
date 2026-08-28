using System.Diagnostics;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Rules;

[Trait("Category", "Scale")]
public sealed class RuleApplicationScaleTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Rules = 318;

    private const int Programmes = 25_608;

    private const int Services = 20;

    [Fact]
    public async Task ASweepAtTheSizeTheGuideActuallyReachesReadsTheChannelsAWholeRunAtATime()
    {
        var streams = new CountedStreams([Terrestrial()]);
        var catalogue = new CountedServices();
        var rules = new HeldRules();
        var programmes = new HeldProgrammes();
        var visits = new HeldStreamVisits();
        var write = new WatchedWrite();
        var reservations = new HeldReservations(write);

        for (int identifier = 1; identifier <= Rules; identifier++)
        {
            rules.Rules.Add(Written(identifier));
        }

        for (int carried = 1; carried <= Programmes; carried++)
        {
            programmes.Programmes.Add(Broadcast(carried));
        }

        var applying = new RuleApplicationService(
            rules,
            programmes,
            reservations,
            visits,
            streams,
            new ReservationSchedulingService(
                reservations,
                new HeldSeating(Seats()),
                new TuningByService { Otherwise = Tunable() },
                write,
                RollingHorizon.Default,
                new FixedClock(Now)),
            new RuleMatcher(new ProgrammeSearchScope(streams, catalogue), new FixedClock(Now)),
            new RuleApplicationSettings { Rows = 5_000 },
            new FixedClock(Now));

        Stopwatch watch = Stopwatch.StartNew();
        RuleApplicationRun run = await applying.EverythingAsync(Cancel);
        watch.Stop();

        Console.WriteLine(
            $"MEASURED rules={Rules} programmes={Programmes} read={run.Read} made={run.Made.Count} "
            + $"streamReads={streams.Reads} catalogueReads={catalogue.Reads} ms={watch.ElapsedMilliseconds}");

        Assert.Equal(Programmes, run.Read);
        Assert.Equal(1, catalogue.Reads);
        Assert.Equal(2, streams.Reads);
    }

    private static Rule Written(int identifier)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            $"rule {identifier}",
            new RuleQuery(identifier % 2 is 0 ? "keyword=hill" : "keyword=hill&type=IsdbT"),
            new Priority(identifier % 90 + 1),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static Programme Broadcast(int carried)
        => Programme.Rehydrate(
            new ProgrammeId(
                new NetworkId(Network),
                new ServiceId(1024 + (carried % Services)),
                new EventId(carried)),
            new TransportStreamId(Carried),
            Now.AddHours(2).AddMinutes(carried),
            Now.AddHours(3).AddMinutes(carried),
            carried % 2560 is 0 ? "a hill walk" : "a river trip",
            "a summary",
            false,
            Now,
            revision: carried);

    private static TunerCapacity Seats()
        => new([new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)], []);

    private static TuningResolution Tunable()
        => TuningResolution.Tunable(
            new CandidateChannelId(Guid.NewGuid()),
            TuningParameters.Terrestrial(27),
            impaired: false);

    private static BroadcastStream Terrestrial()
        => new(
            new NetworkId(Network),
            new TransportStreamId(Carried),
            TuningParameters.Terrestrial(27),
            [.. Enumerable.Range(0, Services).Select(service => new ServiceId(1024 + service))]);
}
