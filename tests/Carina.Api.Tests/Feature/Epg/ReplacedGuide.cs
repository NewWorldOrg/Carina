using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Epg;

/// <summary>A guide in which a programme heard later replaced part of the schedule it was first heard with.</summary>
internal static class ReplacedGuide
{
    public const string TheDay = "from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z";

    public static readonly DateTime Start = new(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

    public static ArchivedProgramme Kept(Programme programme)
        => ArchivedProgramme.Of(new EndedProgramme(programme, programme.EndsAt!.Value), Start.AddDays(2))!;

    public static EpgFeature Replaced()
    {
        var feature = new EpgFeature(
        [
            new BroadcastStream(
                new NetworkId(4),
                new TransportStreamId(32_736),
                TuningParameters.Terrestrial(22),
                [new ServiceId(1049)]),
        ]);

        feature.Programmes.Programmes.AddRange(ReplacedSchedule.On(4, 1049, Start));

        return feature;
    }

    public static string[] Ids(JsonElement programmes)
        => [.. programmes.EnumerateArray().Select(programme => programme.GetProperty("id").GetString() ?? string.Empty)];
}
