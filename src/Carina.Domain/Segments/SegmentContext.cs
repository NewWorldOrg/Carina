using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Segments;

/// <summary>
/// What is known of the recording judged besides its readings: its station and series, where the guide
/// puts its programme's start and end in the recording's own time, the programme's genres, the stretches
/// the catalog heard in it, the logo learned for its station, and the earlier episodes of its series.
/// </summary>
public sealed record SegmentContext
{
    public SegmentContext(
        NetworkId networkId,
        ServiceId serviceId,
        string? series,
        TimeSpan? programmeStarts,
        TimeSpan? programmeEnds,
        IReadOnlyList<ProgrammeGenre> genres,
        IReadOnlyList<CatalogHeard> catalog,
        CornerLogo logo,
        IReadOnlyList<PastEpisode> past)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(genres);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logo);
        ArgumentNullException.ThrowIfNull(past);

        if (programmeEnds <= programmeStarts)
        {
            throw new ArgumentException("A programme ends after it starts.", nameof(programmeEnds));
        }

        NetworkId = networkId;
        ServiceId = serviceId;
        Series = series;
        ProgrammeStarts = programmeStarts;
        ProgrammeEnds = programmeEnds;
        Genres = [.. genres];
        Catalog = [.. catalog.OrderBy(heard => heard.Starts).ThenBy(heard => heard.Ends)];
        Logo = logo;
        Past = [.. past];
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public string? Series { get; }

    public TimeSpan? ProgrammeStarts { get; }

    public TimeSpan? ProgrammeEnds { get; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; }

    public IReadOnlyList<CatalogHeard> Catalog { get; }

    public CornerLogo Logo { get; }

    public IReadOnlyList<PastEpisode> Past { get; }

    public string Station => SegmentModel.StationKey(NetworkId, ServiceId);

    public static SegmentContext Of(ProgrammeCopy programme, IReadOnlyList<CatalogHeard> catalog, CornerLogo logo, IReadOnlyList<PastEpisode> past)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new SegmentContext(
            programme.NetworkId,
            programme.ServiceId,
            programme.SeriesName ?? SeriesNames.Of(programme.Name),
            programme.ProgrammeStartsAt - programme.RecordingStartedAt,
            programme.ProgrammeEndsAt - programme.RecordingStartedAt,
            programme.Genres,
            catalog,
            logo,
            past);
    }
}
