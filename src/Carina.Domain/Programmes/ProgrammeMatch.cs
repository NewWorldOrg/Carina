using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class ProgrammeMatch
{
    private ProgrammeMatch()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public DateTime StartsAt { get; private set; }

    public DateTime? EndsAt { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public bool IsShadow { get; private set; }

    public bool HasSubtitles { get; private set; }

    public ProgrammeSource? Source { get; private set; }

    public long? Revision { get; private set; }

    public bool IsArchived { get; private set; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; private set; } = [];

    public IReadOnlyList<ProgrammeItem> Items { get; private set; } = [];

    public IReadOnlyList<RelatedProgramme> Related { get; private set; } = [];

    public ProgrammeId Id => new(NetworkId, ServiceId, EventId);

    public static ProgrammeMatch Of(Programme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ProgrammeMatch
        {
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            StartsAt = programme.StartsAt,
            EndsAt = programme.EndsAt,
            Name = programme.Name,
            Summary = programme.Summary,
            IsShadow = programme.IsShadow,
            HasSubtitles = programme.HasSubtitles,
            Source = programme.Source,
            Revision = programme.Revision,
            IsArchived = false,
            Genres = programme.Genres,
            Items = programme.Items,
            Related = programme.Related,
        };
    }

    public static ProgrammeMatch Of(ArchivedProgramme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ProgrammeMatch
        {
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            StartsAt = programme.StartsAt,
            EndsAt = programme.EndsAt,
            Name = programme.Name,
            Summary = programme.Summary,
            IsShadow = false,
            HasSubtitles = programme.HasSubtitles,
            Source = null,
            Revision = null,
            IsArchived = true,
            Genres = programme.Genres,
            Items = programme.Items,
            Related = [],
        };
    }
}

public interface IProgrammeSearchRepository
{
    Task<PaginatedList<ProgrammeMatch>> SearchAsync(ProgrammeSearch search, CancellationToken cancellationToken);
}
