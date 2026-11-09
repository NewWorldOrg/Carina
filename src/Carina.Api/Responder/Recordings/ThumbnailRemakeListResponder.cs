using Carina.Api.Services;

namespace Carina.Api.Responder.Recordings;

/// <summary>
/// One row of the answer to drawing pictures again: the recording as it was asked, what became of it, and
/// why when nothing was drawn.
/// </summary>
public sealed record ThumbnailRemakeRowResponder(string Id, ThumbnailRemakeOutcome Outcome, string Message)
{
    public static ThumbnailRemakeRowResponder Of(ThumbnailRemakeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new ThumbnailRemakeRowResponder(row.Id.Wire, row.Outcome, row.Message);
    }
}

public sealed record ThumbnailRemakeListResponder(IReadOnlyList<ThumbnailRemakeRowResponder> Items)
{
    public static ThumbnailRemakeListResponder Of(IReadOnlyList<ThumbnailRemakeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new ThumbnailRemakeListResponder([.. rows.Select(ThumbnailRemakeRowResponder.Of)]);
    }
}
