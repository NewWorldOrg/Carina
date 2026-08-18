using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Responder.Epg;

public sealed record ProgrammeGenreResponder(int Kind, int Sort)
{
    public static ProgrammeGenreResponder Of(ProgrammeGenre genre)
    {
        ArgumentNullException.ThrowIfNull(genre);

        return new ProgrammeGenreResponder(genre.Kind, genre.Sort);
    }
}

public sealed record ProgrammeItemResponder(string Heading, string Text)
{
    public static ProgrammeItemResponder Of(ProgrammeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ProgrammeItemResponder(item.Heading, item.Text);
    }
}

public sealed record RelatedProgrammeResponder(
    int NetworkId,
    int ServiceId,
    int EventId,
    RelationKind Kind)
{
    public static RelatedProgrammeResponder Of(RelatedProgramme related)
    {
        ArgumentNullException.ThrowIfNull(related);

        return new RelatedProgrammeResponder(
            related.NetworkId,
            related.ServiceId,
            related.EventId,
            related.Kind);
    }
}

public sealed record ProgrammeResponder(
    string Id,
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string Name,
    string Summary,
    bool IsShadow,
    bool HasSubtitles,
    ProgrammeSource Source,
    long Revision,
    bool IsArchived,
    IReadOnlyList<ProgrammeGenreResponder> Genres,
    IReadOnlyList<ProgrammeItemResponder> Items,
    IReadOnlyList<RelatedProgrammeResponder> Related)
{
    public static ProgrammeResponder Of(ArchivedProgramme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ProgrammeResponder(
            ProgrammeIdText.Of(new ProgrammeId(programme.NetworkId, programme.ServiceId, programme.EventId)),
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            new DateTimeOffset(programme.StartsAt, TimeSpan.Zero),
            new DateTimeOffset(programme.EndsAt, TimeSpan.Zero),
            programme.Name,
            programme.Summary,
            false,
            programme.HasSubtitles,
            ProgrammeSource.ScheduleBasic,
            0,
            true,
            [.. programme.Genres.Select(ProgrammeGenreResponder.Of)],
            [.. programme.Items.Select(ProgrammeItemResponder.Of)],
            []);
    }

    public static ProgrammeResponder Of(Programme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ProgrammeResponder(
            ProgrammeIdText.Of(programme.Id),
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            new DateTimeOffset(programme.StartsAt, TimeSpan.Zero),
            programme.EndsAt is null ? null : new DateTimeOffset(programme.EndsAt.Value, TimeSpan.Zero),
            programme.Name,
            programme.Summary,
            programme.IsShadow,
            programme.HasSubtitles,
            programme.Source,
            programme.Revision,
            false,
            [.. programme.Genres.Select(ProgrammeGenreResponder.Of)],
            [.. programme.Items.Select(ProgrammeItemResponder.Of)],
            [.. programme.Related.Select(RelatedProgrammeResponder.Of)]);
    }
}

public sealed record GuideServiceResponder(int NetworkId, int ServiceId);

public sealed record GuideResponder(
    IReadOnlyList<GuideServiceResponder> Services,
    IReadOnlyList<ProgrammeResponder> Programmes)
{
    public static GuideResponder Of(GuidePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GuideResponder(
            [
                .. page.Streams
                    .SelectMany(stream => stream.Services.Select(service =>
                        new GuideServiceResponder(stream.NetworkId.Value, service.Value)))
                    .OrderBy(service => service.NetworkId)
                    .ThenBy(service => service.ServiceId),
            ],
            [
                .. page.Programmes.Select(ProgrammeResponder.Of)
                    .Concat(page.Archived.Select(ProgrammeResponder.Of))
                    .OrderBy(programme => programme.StartsAt)
                    .ThenBy(programme => programme.ServiceId)
                    .ThenBy(programme => programme.EventId),
            ]);
    }
}
