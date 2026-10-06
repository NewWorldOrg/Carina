using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

/// <summary>
/// A page of recordings, and the place just after its last row when more come after it.
/// </summary>
public sealed record RecordingListing(PaginatedList<Recording> Found, RecordingCursor? Next)
{
    /// <summary>
    /// The page made of the rows read in order from where the query starts, one more than a page when more
    /// come after it. <paramref name="ahead"/> is how many matching rows come before the first of them, which
    /// numbers the page.
    /// </summary>
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
