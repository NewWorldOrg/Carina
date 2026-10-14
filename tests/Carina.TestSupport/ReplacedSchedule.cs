using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.TestSupport;

/// <summary>
/// One service's afternoon as the guide holds it after the station replaced it: nine programmes of the schedule
/// heard the day before, the replacement that covers them and runs ten minutes over, heard today, the programme
/// announced to follow it, and the one before, which aired before anything changed.
/// </summary>
public static class ReplacedSchedule
{
    public const int Before = 1;

    public const int Replacement = 101;

    public const int After = 102;

    public static readonly IReadOnlyList<int> Replaced = [2, 3, 4, 5, 6, 7, 8, 9, 10];

    public static readonly IReadOnlyList<int> Standing = [Before, Replacement, After];

    private static readonly int[] ReplacedMinutes = [0, 30, 60, 85, 120, 150, 180, 185, 210, 240];

    public static DateTime HeardTheDayBefore(DateTime start) => start.AddHours(-17);

    public static DateTime HeardToday(DateTime start) => start.AddHours(-1);

    public static DateTime ReplacementEnds(DateTime start) => start.AddMinutes(250);

    public static IReadOnlyList<Programme> On(int network, int service, DateTime start)
        =>
        [
            Announced(network, service, Before, "前の番組", start.AddHours(-1), start, HeardTheDayBefore(start)),
            .. ReplacedMinutes.Zip(ReplacedMinutes.Skip(1)).Select((minutes, index) => Announced(
                network,
                service,
                Replaced[index],
                $"元の予定の番組{index + 1}",
                start.AddMinutes(minutes.First),
                start.AddMinutes(minutes.Second),
                HeardTheDayBefore(start))),
            Announced(
                network,
                service,
                Replacement,
                "差し替えの番組",
                start,
                ReplacementEnds(start),
                HeardToday(start)),
            Announced(
                network,
                service,
                After,
                "次の番組",
                ReplacementEnds(start),
                ReplacementEnds(start).AddMinutes(50),
                HeardToday(start)),
        ];

    private static Programme Announced(
        int network,
        int service,
        int eventId,
        string name,
        DateTime startsAt,
        DateTime endsAt,
        DateTime heard)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(eventId)),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            name,
            $"{name}の内容",
            isShadow: false,
            heard,
            sounds: 1,
            audio: eventId == Replacement ? AudioMode.Stereo : AudioMode.DualMono,
            lastHeardAt: heard);
}
