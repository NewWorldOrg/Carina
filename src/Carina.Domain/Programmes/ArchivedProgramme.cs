using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class ArchivedProgramme
{
    private ArchivedProgramme()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public bool HasSubtitles { get; private set; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; private set; } = [];

    public IReadOnlyList<ProgrammeItem> Items { get; private set; } = [];

    public DateTime ArchivedAt { get; private set; }

    public static ArchivedProgramme? Of(Programme programme, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(programme);

        if (programme.IsShadow || programme.EndsAt is not { } endsAt)
        {
            return null;
        }

        return Rehydrate(
            programme.NetworkId,
            programme.ServiceId,
            programme.EventId,
            programme.StartsAt,
            endsAt,
            programme.Name,
            programme.Summary,
            programme.HasSubtitles,
            programme.Genres,
            programme.Items,
            at);
    }

    public static ArchivedProgramme Rehydrate(
        NetworkId networkId,
        ServiceId serviceId,
        EventId eventId,
        DateTime startsAt,
        DateTime endsAt,
        string name,
        string summary,
        bool hasSubtitles,
        IReadOnlyList<ProgrammeGenre>? genres,
        IReadOnlyList<ProgrammeItem>? items,
        DateTime archivedAt)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(eventId);

        return new ArchivedProgramme
        {
            NetworkId = networkId,
            ServiceId = serviceId,
            EventId = eventId,
            StartsAt = UtcTimes.Required(startsAt, nameof(startsAt)),
            EndsAt = UtcTimes.Required(endsAt, nameof(endsAt)),
            Name = name ?? string.Empty,
            Summary = summary ?? string.Empty,
            HasSubtitles = hasSubtitles,
            Genres = genres ?? [],
            Items = items ?? [],
            ArchivedAt = UtcTimes.Required(archivedAt, nameof(archivedAt)),
        };
    }

    public bool AbsorbTheRicherOf(ArchivedProgramme arriving)
    {
        ArgumentNullException.ThrowIfNull(arriving);

        string name = Richer(Name, arriving.Name);
        string summary = Richer(Summary, arriving.Summary);
        IReadOnlyList<ProgrammeGenre> genres = Richer(Genres, arriving.Genres);
        IReadOnlyList<ProgrammeItem> items = Richer(Items, arriving.Items);
        bool hasSubtitles = HasSubtitles || arriving.HasSubtitles;

        if (Name == name
            && Summary == summary
            && HasSubtitles == hasSubtitles
            && Genres.SequenceEqual(genres)
            && Items.SequenceEqual(items)
            && EndsAt == arriving.EndsAt)
        {
            return false;
        }

        Name = name;
        Summary = summary;
        HasSubtitles = hasSubtitles;
        Genres = genres;
        Items = items;
        EndsAt = arriving.EndsAt;

        return true;
    }

    private static string Richer(string held, string arriving)
        => arriving.Length > held.Length ? arriving : held;

    private static IReadOnlyList<T> Richer<T>(IReadOnlyList<T> held, IReadOnlyList<T> arriving)
        => arriving.Count > held.Count ? arriving : held;
}

public interface IArchivedProgrammeRepository
{
    Task<IReadOnlyList<ArchivedProgramme>> ListAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);

    Task<int> KeepAsync(IReadOnlyList<ArchivedProgramme> programmes, CancellationToken cancellationToken);

    Task<int> ForgetBeforeAsync(DateTime at, CancellationToken cancellationToken);

    Task<int> ForgetServiceAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken);
}
