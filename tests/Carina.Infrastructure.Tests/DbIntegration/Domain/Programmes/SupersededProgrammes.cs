using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Programmes;

/// <summary>
/// A schedule and the programmes that replaced parts of it, heard at different times, written to the guide and to the
/// archive so that each repository can be asked what it leaves out.
/// </summary>
internal static class SupersededProgrammes
{
    public const int Service = 1049;

    public const int Neighbour = 1050;

    public static readonly DateTime Start = new(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime HeardTwoDaysBefore = Start.AddDays(-2);

    public static readonly DateTime HeardTheDayBefore = Start.AddDays(-1);

    public static readonly DateTime HeardToday = Start.AddHours(-1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    public static async Task KeepAsync(
        ServiceProvider container,
        IReadOnlyList<Programme> held,
        IReadOnlyList<ArchivedProgramme> kept)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IProgrammeRepository programmes = writing.ServiceProvider.GetRequiredService<IProgrammeRepository>();

        foreach (Programme programme in held)
        {
            await programmes.AddAsync(programme, Cancel);
        }

        await writing.ServiceProvider.GetRequiredService<IArchivedProgrammeRepository>().KeepAsync(kept, Cancel);
    }

    public static async Task KeepAnOldScheduleBesideAReplacementAsync(ServiceProvider container, int network, int stillInTheGuide)
    {
        Programme[] schedule = [.. ReplacedSchedule.On(network, Service, Start)];
        Programme[] another = [.. ReplacedSchedule.On(stillInTheGuide, Service, Start)];

        await KeepAsync(
            container,
            [another.Single(programme => programme.EventId.Value == ReplacedSchedule.Replacement)],
            [
                .. schedule.Select(programme => Unheard(Moved(programme), heardAnyway: programme.EventId.Value
                    is ReplacedSchedule.Replacement or ReplacedSchedule.After)),
                .. another
                    .Where(programme => ReplacedSchedule.Replaced.Contains(programme.EventId.Value))
                    .Select(programme => Unheard(Moved(programme))),
            ]);
    }

    public static async Task KeepTwoProgrammesWithoutAHeardTimeAsync(ServiceProvider container, int network)
        => await KeepAsync(
            container,
            [],
            [
                Unheard(Moved(On(network, 1, Start, Start.AddHours(1), null))),
                Unheard(Moved(On(network, 2, Start.AddMinutes(30), Start.AddHours(2), null))),
            ]);


    public static Programme[] Varied(int network)
        =>
        [
            On(network, 1, Start, Start.AddHours(1), HeardToday),
            On(network, 2, Start.AddMinutes(30), Start.AddHours(2), HeardToday),

            On(network, 3, Start.AddHours(3), Start.AddHours(4), HeardToday),
            On(network, 4, Start.AddHours(2), Start.AddHours(3), HeardTheDayBefore),
            On(network, 5, Start.AddHours(4), Start.AddHours(5), HeardTheDayBefore),

            On(network, 6, Start.AddHours(6), null, HeardToday),
            On(network, 7, Start.AddHours(6).AddMinutes(59), Start.AddHours(7).AddMinutes(30), HeardTheDayBefore),
            On(network, 8, Start.AddHours(7), Start.AddHours(7).AddMinutes(30), HeardTheDayBefore),

            On(network, 9, Start.AddHours(9), null, HeardTheDayBefore),
            On(network, 10, Start.AddHours(9).AddMinutes(59), Start.AddHours(10).AddMinutes(30), HeardToday),
            On(network, 11, Start.AddHours(11), null, HeardTheDayBefore),
            On(network, 12, Start.AddHours(12), Start.AddHours(13), HeardToday),

            On(network, 13, Start.AddHours(14), Start.AddHours(15), HeardToday, shadow: true),
            On(network, 14, Start.AddHours(14), Start.AddHours(15), HeardTheDayBefore),
            On(network, 15, Start.AddHours(16), Start.AddHours(17), HeardTheDayBefore, shadow: true),
            On(network, 16, Start.AddHours(16), Start.AddHours(17), HeardToday),

            On(network, 17, Start.AddHours(18), Start.AddHours(19), null),
            On(network, 18, Start.AddHours(18).AddMinutes(30), Start.AddHours(20), HeardTheDayBefore),
            On(network, 19, Start.AddHours(19).AddMinutes(30), Start.AddHours(21), null),
            On(network, 20, Start.AddHours(20).AddMinutes(30), Start.AddHours(21), null),

            On(network, 21, Start.AddHours(22), Start.AddHours(23), HeardTheDayBefore, service: Neighbour),
            On(network, 22, Start.AddHours(22), Start.AddHours(23), HeardToday),

            On(network, 23, Start.AddHours(24), Start.AddHours(28).AddMinutes(10), HeardToday),
            On(network, 24, Start.AddHours(27).AddMinutes(5), Start.AddHours(28), HeardTheDayBefore),
            On(network, 25, Start.AddHours(28), Start.AddHours(28).AddMinutes(15), HeardTheDayBefore),
            On(network, 26, Start.AddHours(28).AddMinutes(10), Start.AddHours(29), HeardToday),

            On(network, 27, Start.AddHours(30), Start.AddHours(32), HeardTwoDaysBefore),
            On(network, 28, Start.AddHours(31), Start.AddHours(33), HeardTheDayBefore),
            On(network, 29, Start.AddHours(32).AddMinutes(30), Start.AddHours(34), HeardToday),

            On(network, 30, Start.AddHours(-20), Start.AddHours(-19), HeardTwoDaysBefore),
            On(network, 31, Start.AddHours(-19), Start.AddHours(-18), HeardToday),
        ];

    public static ArchivedProgramme[] Kept(int network)
        =>
        [
            Kept(network, 90, Start.AddHours(-20).AddMinutes(30), Start.AddHours(-19).AddMinutes(30), null),

            Kept(network, 41, Start.AddHours(-48), Start.AddHours(-47), HeardToday.AddDays(-2)),
            Kept(network, 42, Start.AddHours(-48), Start.AddHours(-47), HeardTwoDaysBefore.AddDays(-2)),
            Kept(network, 43, Start.AddHours(-47), Start.AddHours(-46), HeardTwoDaysBefore.AddDays(-2)),

            Kept(network, 44, Start.AddHours(-45), Start.AddHours(-44), null),
            Kept(network, 45, Start.AddHours(-44).AddMinutes(30), Start.AddHours(-43), null),
            Kept(network, 46, Start.AddHours(-44).AddMinutes(30), Start.AddHours(-43), HeardTheDayBefore.AddDays(-2)),

            Kept(network, 47, Start.AddHours(-30), Start.AddHours(-29), HeardTwoDaysBefore),
            Kept(network, 48, Start.AddHours(-30), Start.AddHours(-29), HeardTwoDaysBefore, service: Neighbour),
            Kept(network, 49, Start.AddHours(-29), Start.AddHours(-28), null),
            Kept(network, 50, Start.AddHours(-29).AddMinutes(30), Start.AddHours(-28), null),

            Kept(network, 51, Start.AddHours(-26), Start.AddHours(-25), HeardTwoDaysBefore),
            Kept(network, 52, Start.AddHours(-25), Start.AddHours(-24), HeardTwoDaysBefore),
        ];

    public static ArchivedProgramme Kept(
        int network,
        int eventId,
        DateTime startsAt,
        DateTime endsAt,
        DateTime? heard,
        int service = Service)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(service),
            new EventId(eventId),
            startsAt,
            endsAt,
            $"保存された番組{eventId}",
            string.Empty,
            false,
            [],
            [],
            Start,
            heard);

    public static ArchivedProgramme Moved(Programme programme)
        => ArchivedProgramme.Of(new EndedProgramme(programme, programme.EndsAt!.Value), Start.AddDays(2))!;

    public static ArchivedProgramme Unheard(ArchivedProgramme kept, bool heardAnyway = false)
        => heardAnyway
            ? kept
            : ArchivedProgramme.Rehydrate(
                kept.NetworkId,
                kept.ServiceId,
                kept.EventId,
                kept.StartsAt,
                kept.EndsAt,
                kept.Name,
                kept.Summary,
                kept.HasSubtitles,
                kept.Genres,
                kept.Items,
                kept.ArchivedAt);

    public static ProgrammeSearch Everything(int network)
        => ProgrammeSearch.For(
            null,
            Start.AddDays(-3),
            Start.AddDays(3),
            perPage: 100,
            conditions: new ProgrammeConditions
            {
                Channels = [new ProgrammeService(network, Service), new ProgrammeService(network, Neighbour)],
            })!;

    public static string[] Faces(IEnumerable<ArchivedProgramme> programmes)
        =>
        [
            .. programmes
                .Select(programme => $"{programme.ServiceId.Value}-{programme.EventId.Value}")
                .Order(StringComparer.Ordinal),
        ];

    public static int[] Events(IEnumerable<ArchivedProgramme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    public static string[] Faces(IEnumerable<Programme> programmes)
        =>
        [
            .. programmes
                .Select(programme => $"{programme.ServiceId.Value}-{programme.EventId.Value}")
                .Order(StringComparer.Ordinal),
        ];

    public static string[] Names(PaginatedList<ProgrammeMatch> found)
        => [.. found.Items.Select(match => match.Name)];

    public static int[] Events(IEnumerable<Programme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    public static ProgrammeId Id(int network, int service, int eventId)
        => new(new NetworkId(network), new ServiceId(service), new EventId(eventId));

    public static Programme On(
        int network,
        int eventId,
        DateTime startsAt,
        DateTime? endsAt,
        DateTime? heard,
        bool shadow = false,
        int service = Service)
        => Programme.Rehydrate(
            Id(network, service, eventId),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            shadow ? string.Empty : $"番組{eventId:D2}",
            string.Empty,
            shadow,
            HeardTwoDaysBefore,
            lastHeardAt: heard);
}
