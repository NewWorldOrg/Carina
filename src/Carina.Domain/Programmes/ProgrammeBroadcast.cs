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

    public AudioMode Audio { get; init; }

    public int Sounds { get; init; }

    public VideoMode Video { get; init; }

    public AspectRatio Aspect { get; init; }

    public ProgrammeRunning Running { get; init; }

    public ProgrammeSource Source { get; init; } = ProgrammeSource.ScheduleBasic;

    public static readonly TimeSpan FurthestBehind = TimeSpan.FromDays(1);

    public static readonly TimeSpan FurthestAhead = TimeSpan.FromDays(10);

    public static ProgrammeBroadcast Announced(
        ProgrammeId id,
        TransportStreamId transportStreamId,
        DateTime startsAt,
        DateTime? endsAt,
        string name,
        string summary,
        ProgrammeSource source,
        ProgrammeRunning running,
        IReadOnlyList<AnnouncedEventGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(groups);

        return new ProgrammeBroadcast(
            id,
            transportStreamId,
            startsAt,
            endsAt,
            name,
            summary,
            name.Length == 0 && groups.Any(group => group.Kind is RelationKind.Shared))
        {
            Related = [.. RelatedOf(id, groups)],
            Running = source is ProgrammeSource.PresentFollowing ? running : ProgrammeRunning.Undetermined,
            Source = source,
        };
    }

    public ProgrammeBroadcast MergedWith(ProgrammeBroadcast arriving)
    {
        ArgumentNullException.ThrowIfNull(arriving);

        ProgrammeBroadcast named = arriving.Name.Length > 0 ? arriving : this;

        return named with
        {
            EndsAt = named.EndsAt ?? EndsAt ?? arriving.EndsAt,
            Summary = FirstWritten(named.Summary, Summary, arriving.Summary),
            Genres = Genres.Count > 0 ? Genres : arriving.Genres,
            Items = Items.Count > 0 ? Items : arriving.Items,
            Related = Related.Count > 0 ? Related : arriving.Related,
            HasSubtitles = HasSubtitles || arriving.HasSubtitles,
            Audio = Audio is AudioMode.Undetermined ? arriving.Audio : Audio,
            Sounds = Sounds == 0 ? arriving.Sounds : Sounds,
            Video = Video is VideoMode.Undetermined ? arriving.Video : Video,
            Aspect = Aspect is AspectRatio.Undetermined ? arriving.Aspect : Aspect,
            Running = Running is ProgrammeRunning.Undetermined ? arriving.Running : Running,
            IsShadow = IsShadow && arriving.IsShadow,
        };
    }

    public bool StartsWithinTheHorizonAt(DateTime now)
    {
        UtcTimes.Required(now, nameof(now));

        return StartsAt >= now - FurthestBehind && StartsAt <= now + FurthestAhead;
    }

    private static string FirstWritten(string named, string seen, string arriving)
    {
        if (named.Length > 0)
        {
            return named;
        }

        return seen.Length > 0 ? seen : arriving;
    }

    private static IEnumerable<RelatedProgramme> RelatedOf(ProgrammeId id, IReadOnlyList<AnnouncedEventGroup> groups)
        => groups.SelectMany(group => group.Events
            .Where(announced => announced.NetworkId is not null
                || announced.ServiceId != id.ServiceId.Value
                || announced.EventId != id.EventId.Value)
            .Select(announced => new RelatedProgramme(
                announced.NetworkId ?? id.NetworkId.Value,
                announced.ServiceId,
                announced.EventId,
                group.Kind)));
}
