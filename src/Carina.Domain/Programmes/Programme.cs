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

    public AudioMode Audio { get; private set; }

    public int Sounds { get; private set; }

    public ProgrammeSource Source { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? LastHeardAt { get; private set; }

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
            broadcast.Audio,
            broadcast.Sounds,
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
        AudioMode audio = AudioMode.Undetermined,
        int sounds = 0,
        ProgrammeSource source = ProgrammeSource.ScheduleBasic,
        long revision = 0,
        DateTime? lastHeardAt = null)
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
            Audio = audio,
            Sounds = sounds,
            Source = source,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
            LastHeardAt = UtcTimes.Optional(lastHeardAt, nameof(lastHeardAt)),
            Revision = revision,
        };
    }

    /// <summary>
    /// Writes down that this programme was named again by a reading that heard the whole of its
    /// service's announced schedule. It is the one mark that separates "still announced" from "no
    /// longer announced": the row itself never goes away on its own, and <c>UpdatedAt</c> stands
    /// still while nothing about the programme changes, so neither of them can tell the two apart.
    /// No mark at all means no whole reading has ever named this programme, which says nothing
    /// either way.
    /// </summary>
    public void Heard(DateTime at)
    {
        LastHeardAt = UtcTimes.Required(at, nameof(at));
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
        AudioMode audio = Kept(Audio, broadcast.Audio);
        int sounds = Kept(Sounds, broadcast.Sounds);

        if (TransportStreamId.Equals(broadcast.TransportStreamId)
            && StartsAt == startsAt
            && EndsAt == endsAt
            && Name == name
            && Summary == summary
            && IsShadow == broadcast.IsShadow
            && HasSubtitles == broadcast.HasSubtitles
            && Audio == audio
            && Sounds == sounds
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
        Audio = audio;
        Sounds = sounds;
        Source = broadcast.Source;
        UpdatedAt = at;

        return true;
    }

    private static DateTime? Settled(DateTime startsAt, DateTime? endsAt)
        => endsAt is { } ends && ends > startsAt ? ends : null;

    private static string Kept(string held, string arriving) => arriving.Length == 0 ? held : arriving;

    private static AudioMode Kept(AudioMode held, AudioMode arriving)
        => arriving is AudioMode.Undetermined ? held : arriving;

    private static int Kept(int held, int arriving) => arriving == 0 ? held : arriving;

    private static IReadOnlyList<T> Kept<T>(IReadOnlyList<T> held, IReadOnlyList<T> arriving)
        => arriving.Count == 0 ? held : arriving;

    private static string Clamped(string text, int most) => text.Length <= most ? text : text[..most];
}
