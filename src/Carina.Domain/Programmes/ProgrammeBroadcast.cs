using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed record ProgrammeBroadcast(
    ProgrammeId Id,
    TransportStreamId TransportStreamId,
    DateTime StartsAt,
    DateTime? EndsAt,
    string Name,
    string Summary,
    bool IsShadow)
{
    public IReadOnlyList<ProgrammeGenre> Genres { get; init; } = [];

    public IReadOnlyList<ProgrammeItem> Items { get; init; } = [];

    public IReadOnlyList<RelatedProgramme> Related { get; init; } = [];

    public bool HasSubtitles { get; init; }

    public ProgrammeSource Source { get; init; } = ProgrammeSource.ScheduleBasic;

    public static readonly TimeSpan FurthestBehind = TimeSpan.FromDays(1);

    public static readonly TimeSpan FurthestAhead = TimeSpan.FromDays(10);

    public bool StartsWithinTheHorizonAt(DateTime now)
    {
        UtcTimes.Required(now, nameof(now));

        return StartsAt >= now - FurthestBehind && StartsAt <= now + FurthestAhead;
    }
}
