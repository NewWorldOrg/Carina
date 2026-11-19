using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingListing(PaginatedList<Recording> Found, RecordingCursor? Next)
{
    public static RecordingListing Of(IReadOnlyList<Recording> read, int total, int ahead, RecordingQuery query)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(query);

        Recording[] page = [.. read.Take(query.PerPage)];
        RecordingCursor? next = read.Count > query.PerPage ? RecordingCursor.Past(page[^1], query) : null;

        return new RecordingListing(
            new PaginatedList<Recording>(page, total, (ahead / query.PerPage) + 1, query.PerPage),
            next);
    }
}
