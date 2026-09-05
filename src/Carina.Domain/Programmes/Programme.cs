using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class Programme
{
    public const int NameMaxLength = 512;

    public const int SummaryMaxLength = 4096;

    private Programme()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public ProgrammeId Id => new(NetworkId, ServiceId, EventId);

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public DateTime StartsAt { get; private set; }

    public DateTime? EndsAt { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public bool IsShadow { get; private set; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; private set; } = [];

    public IReadOnlyList<ProgrammeItem> Items { get; private set; } = [];

    public IReadOnlyList<RelatedProgramme> Related { get; private set; } = [];

    public bool HasSubtitles { get; private set; }

    public ProgrammeSource Source { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public long Revision { get; private set; }

    public static Programme Discover(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        return Rehydrate(
            broadcast.Id,
            broadcast.TransportStreamId,
            broadcast.StartsAt,
            broadcast.EndsAt,
            broadcast.Name,
            broadcast.Summary,
            broadcast.IsShadow,
            at,
            broadcast.Genres,
            broadcast.Items,
            broadcast.Related,
            broadcast.HasSubtitles,
            broadcast.Source);
    }

    public static Programme Rehydrate(
        ProgrammeId id,
        TransportStreamId transportStreamId,
        DateTime startsAt,
        DateTime? endsAt,
        string name,
        string summary,
        bool isShadow,
        DateTime updatedAt,
        IReadOnlyList<ProgrammeGenre>? genres = null,
        IReadOnlyList<ProgrammeItem>? items = null,
        IReadOnlyList<RelatedProgramme>? related = null,
        bool hasSubtitles = false,
        ProgrammeSource source = ProgrammeSource.ScheduleBasic,
        long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(transportStreamId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        return new Programme
        {
            NetworkId = id.NetworkId,
            ServiceId = id.ServiceId,
            EventId = id.EventId,
            TransportStreamId = transportStreamId,
            StartsAt = UtcTimes.Required(startsAt, nameof(startsAt)),
            EndsAt = Settled(startsAt, UtcTimes.Optional(endsAt, nameof(endsAt))),
            Name = Clamped(name, NameMaxLength),
            Summary = Clamped(summary, SummaryMaxLength),
            IsShadow = isShadow,
            Genres = genres ?? [],
            Items = items ?? [],
            Related = related ?? [],
            HasSubtitles = hasSubtitles,
            Source = source,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
            Revision = revision,
        };
    }

    public void MarkRevision(long revision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);

        Revision = revision;
    }

    public bool Absorb(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);
        UtcTimes.Required(at, nameof(at));

        if (!Id.Equals(broadcast.Id))
        {
            throw new ArgumentException("That broadcast describes another programme.", nameof(broadcast));
        }

        DateTime startsAt = UtcTimes.Required(broadcast.StartsAt, nameof(broadcast));
        string name = Kept(Name, Clamped(broadcast.Name, NameMaxLength));
        string summary = Kept(Summary, Clamped(broadcast.Summary, SummaryMaxLength));
        DateTime? told = Settled(startsAt, UtcTimes.Optional(broadcast.EndsAt, nameof(broadcast)));
        DateTime? endsAt = Settled(startsAt, told ?? EndsAt);
        IReadOnlyList<ProgrammeGenre> genres = Kept(Genres, broadcast.Genres);
        IReadOnlyList<ProgrammeItem> items = Kept(Items, broadcast.Items);
        IReadOnlyList<RelatedProgramme> related = Kept(Related, broadcast.Related);

        if (TransportStreamId.Equals(broadcast.TransportStreamId)
            && StartsAt == startsAt
            && EndsAt == endsAt
            && Name == name
            && Summary == summary
            && IsShadow == broadcast.IsShadow
            && HasSubtitles == broadcast.HasSubtitles
            && Source == broadcast.Source
            && Genres.SequenceEqual(genres)
            && Items.SequenceEqual(items)
            && Related.SequenceEqual(related))
        {
            return false;
        }

        TransportStreamId = broadcast.TransportStreamId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Name = name;
        Summary = summary;
        IsShadow = broadcast.IsShadow;
        Genres = genres;
        Items = items;
        Related = related;
        HasSubtitles = broadcast.HasSubtitles;
        Source = broadcast.Source;
        UpdatedAt = at;

        return true;
    }

    private static DateTime? Settled(DateTime startsAt, DateTime? endsAt)
        => endsAt is { } ends && ends > startsAt ? ends : null;

    private static string Kept(string held, string arriving) => arriving.Length == 0 ? held : arriving;

    private static IReadOnlyList<T> Kept<T>(IReadOnlyList<T> held, IReadOnlyList<T> arriving)
        => arriving.Count == 0 ? held : arriving;

    private static string Clamped(string text, int most) => text.Length <= most ? text : text[..most];
}
