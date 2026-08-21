using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Api.Responder.Epg;

public sealed record ProgrammeSearchResponder(
    IReadOnlyList<ProgrammeResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static ProgrammeSearchResponder Of(PaginatedList<ProgrammeMatch> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new ProgrammeSearchResponder(
            [.. found.Items.Select(ProgrammeResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
