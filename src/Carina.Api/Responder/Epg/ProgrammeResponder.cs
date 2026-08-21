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
    public static ProgrammeResponder Of(Programme programme)
        => Of(ProgrammeMatch.Of(programme));

    public static ProgrammeResponder Of(ArchivedProgramme programme)
        => Of(ProgrammeMatch.Of(programme));

    public static ProgrammeResponder Of(ProgrammeMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new ProgrammeResponder(
            ProgrammeIdText.Of(match.Id),
            match.NetworkId.Value,
            match.ServiceId.Value,
            match.EventId.Value,
            new DateTimeOffset(match.StartsAt, TimeSpan.Zero),
            match.EndsAt is null ? null : new DateTimeOffset(match.EndsAt.Value, TimeSpan.Zero),
            match.Name,
            match.Summary,
            match.IsShadow,
            match.HasSubtitles,
            match.Source ?? ProgrammeSource.ScheduleBasic,
            match.Revision ?? 0,
            match.IsArchived,
            [.. match.Genres.Select(ProgrammeGenreResponder.Of)],
            [.. match.Items.Select(ProgrammeItemResponder.Of)],
            [.. match.Related.Select(RelatedProgrammeResponder.Of)]);
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
